using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using NexusFreelance.Web.Models;

namespace NexusFreelance.Web.Services
{
    public sealed class VnPaySettings
    {
        public string MerchantCode { get; private set; }
        public string Secret { get; private set; }
        public string PublicBase { get; private set; }
        public string Environment { get; private set; }
        public string CheckoutEndpoint => Environment=="sandbox"?"https://sandbox.vnpayment.vn/paymentv2/vpcpay.html":"https://pay.vnpay.vn/vpcpay.html";
        public string ApiEndpoint => Environment=="sandbox"?"https://sandbox.vnpayment.vn/merchant_webapi/api/transaction":"https://merchant.vnpay.vn/merchant_webapi/api/transaction";
        public static string Mode => (string.IsNullOrWhiteSpace(IntegrationSettings.Get("NEXUS_VNPAY_MODE"))?"sandbox":IntegrationSettings.Get("NEXUS_VNPAY_MODE")).Trim().ToLowerInvariant();
        public static bool IsConfigured { get { try {Load();return true;} catch(InvalidOperationException){return false;} } }
        public static VnPaySettings Load()
        {
            var value=new VnPaySettings {MerchantCode=(IntegrationSettings.Get("NEXUS_VNPAY_TMN_CODE")??"").Trim(),Secret=IntegrationSettings.Get("NEXUS_VNPAY_HASH_SECRET")??"",PublicBase=(IntegrationSettings.Get("NEXUS_PUBLIC_BASE_URL")??"").TrimEnd('/'),Environment=Mode};
            Uri uri;
            if(IntegrationSettings.Get("NEXUS_VNPAY_ENABLED")!="true" || !Regex.IsMatch(value.MerchantCode,"^[A-Za-z0-9]{8}$") || value.Secret.Length<32 || value.Secret.Length>256 ||
                !Uri.TryCreate(value.PublicBase,UriKind.Absolute,out uri) || uri.Scheme!="https" || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
                (value.Environment!="sandbox" && value.Environment!="production")) throw new InvalidOperationException("VNPay chưa được cấu hình hợp lệ.");
            if(value.Environment=="production" && (System.Environment.GetEnvironmentVariable("NEXUS_VNPAY_LIVE_ENABLED")!="true" || System.Environment.GetEnvironmentVariable("NEXUS_PSP_MERCHANT_APPROVED")!="true" || uri.IsLoopback))
                throw new InvalidOperationException("VNPay production cần cấu hình merchant đã được phê duyệt và domain HTTPS công khai.");
            return value;
        }
    }
    public static class VnPayCodec
    {
        public static string VietnamDate(DateTime utc) => utc.AddHours(7).ToString("yyyyMMddHHmmss",CultureInfo.InvariantCulture);
        // PHP urlencode / RFC1738, matching the VNPay 2.1.0 canonical query contract.
        public static string Encode(string value) => Uri.EscapeDataString(value).Replace("%20","+").Replace("~","%7E");
        public static string Canonical(IDictionary<string,string> fields) => string.Join("&",fields.Where(x=>x.Key!="vnp_SecureHash" && x.Key!="vnp_SecureHashType" && !string.IsNullOrEmpty(x.Value)).OrderBy(x=>x.Key,StringComparer.Ordinal).Select(x=>Encode(x.Key)+"="+Encode(x.Value)));
        public static string Hmac(string secret,string text)
        {
            using(var h=new HMACSHA512(Encoding.UTF8.GetBytes(secret)))return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant();
        }
        public static bool EqualSignature(string expected,string supplied)
        {
            if(string.IsNullOrEmpty(supplied) || !Regex.IsMatch(supplied,"^[a-fA-F0-9]{128}$"))return false;
            int difference=0;for(int i=0;i<128;i++)difference|=expected[i]^char.ToLowerInvariant(supplied[i]);return difference==0;
        }
        public static Dictionary<string,string> ParseQuery(NameValueCollection query)
        {
            if(query.Count>40)throw new InvalidOperationException("Quá nhiều tham số.");
            var result=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var key in query.AllKeys)
            {
                if(key==null || !key.StartsWith("vnp_",StringComparison.Ordinal))continue;
                var values=query.GetValues(key);
                if(!Regex.IsMatch(key,"^vnp_[A-Za-z0-9_]{1,40}$") || values==null || values.Length!=1 || values[0].Length>600 || values[0].Any(char.IsControl))throw new InvalidOperationException("Tham số không hợp lệ hoặc trùng.");
                result.Add(key,values[0]);
            }
            return result;
        }
        public static string Get(IDictionary<string,string> fields,string key) {string value;return fields.TryGetValue(key,out value)?value:"";}
        public static bool VerifyQuery(IDictionary<string,string> fields,VnPaySettings settings) => EqualSignature(Hmac(settings.Secret,Canonical(fields)),Get(fields,"vnp_SecureHash"));
        public static string Checkout(PaymentIntent payment,VnPaySettings settings,string ip)
        {
            IPAddress address;if(!IPAddress.TryParse(ip,out address))throw new InvalidOperationException("IP thanh toán không hợp lệ.");
            var fields=new Dictionary<string,string> {{"vnp_Version","2.1.0"},{"vnp_Command","pay"},{"vnp_TmnCode",settings.MerchantCode},{"vnp_Amount",(payment.Amount*100).ToString("0",CultureInfo.InvariantCulture)},
                {"vnp_CreateDate",payment.TransactionDate},{"vnp_CurrCode","VND"},{"vnp_IpAddr",address.ToString()},{"vnp_Locale","vn"},{"vnp_OrderInfo","Bao dam hop dong "+payment.ContractId+" moc "+payment.EscrowMilestoneId},
                {"vnp_OrderType","other"},{"vnp_ReturnUrl",settings.PublicBase+"/Payments/Return"},{"vnp_ExpireDate",VietnamDate(payment.ExpiresAtUtc)},{"vnp_TxnRef",payment.TxnRef}};
            var canonical=Canonical(fields);return settings.CheckoutEndpoint+"?"+canonical+"&vnp_SecureHash="+Hmac(settings.Secret,canonical);
        }
        public static bool VerifyApiResponse(IDictionary<string,string> fields,VnPaySettings settings)
        {
            var names=new[]{"vnp_ResponseId","vnp_Command","vnp_ResponseCode","vnp_Message","vnp_TmnCode","vnp_TxnRef","vnp_Amount","vnp_BankCode","vnp_PayDate","vnp_TransactionNo","vnp_TransactionType","vnp_TransactionStatus","vnp_OrderInfo"};
            if(Get(fields,"vnp_Command")=="querydr")names=names.Concat(new[]{"vnp_PromotionCode","vnp_PromotionAmount"}).ToArray();
            return EqualSignature(Hmac(settings.Secret,string.Join("|",names.Select(x=>Get(fields,x)))),Get(fields,"vnp_SecureHash"));
        }
        public static string CheckoutTopup(WalletTopup order, VnPaySettings settings, string ip)
        {
            IPAddress address;
            if (!IPAddress.TryParse(ip, out address)) throw new InvalidOperationException("IP thanh toán không hợp lệ.");
            var fields = new Dictionary<string,string> {
                {"vnp_Version","2.1.0"},{"vnp_Command","pay"},{"vnp_TmnCode",settings.MerchantCode},
                {"vnp_Amount",(order.Amount*100).ToString("0",CultureInfo.InvariantCulture)},
                {"vnp_CreateDate",VietnamDate(order.CreatedAtUtc)},{"vnp_CurrCode","VND"},
                {"vnp_IpAddr",address.ToString()},{"vnp_Locale","vn"},{"vnp_BankCode","VNPAYQR"},
                {"vnp_OrderInfo","Nap vi NexusFreelance " + order.Id},{"vnp_OrderType","other"},
                {"vnp_ReturnUrl",settings.PublicBase+"/Payments/Return"},
                {"vnp_ExpireDate",VietnamDate(order.ExpiresAtUtc)},{"vnp_TxnRef",order.TxnRef}
            };
            var canonical = Canonical(fields);
            return settings.CheckoutEndpoint + "?" + canonical + "&vnp_SecureHash=" + Hmac(settings.Secret,canonical);
        }
    }
    public sealed class VnPayGateway
    {
        private readonly VnPaySettings settings;
        private readonly HttpMessageHandler testTransport;
        public VnPayGateway(VnPaySettings settings,HttpMessageHandler testTransport=null) {this.settings=settings;this.testTransport=testTransport;}
        public async Task<Dictionary<string,string>> Send(PaymentIntent payment,PspRefund refund,string ip)
        {
            IPAddress address;if(!IPAddress.TryParse(ip,out address))throw new InvalidOperationException("IP đối soát không hợp lệ.");
            if(payment.Environment!=settings.Environment || payment.MerchantCode!=settings.MerchantCode)throw new InvalidOperationException("Cấu hình VNPay khác môi trường giao dịch gốc.");
            var command=refund==null?"querydr":"refund";
            var fields=new Dictionary<string,string> {{"vnp_RequestId",refund==null?Guid.NewGuid().ToString("N"):refund.RequestId},{"vnp_Version","2.1.0"},{"vnp_Command",command},{"vnp_TmnCode",settings.MerchantCode},
                {"vnp_TxnRef",payment.TxnRef},{"vnp_TransactionNo",payment.ProviderTransactionNo??""},{"vnp_TransactionDate",payment.TransactionDate},{"vnp_CreateDate",VnPayCodec.VietnamDate(DateTime.UtcNow)},
                {"vnp_IpAddr",address.ToString()},{"vnp_OrderInfo",refund==null?"Doi soat giao dich "+payment.TxnRef:"Hoan tien hop dong "+payment.ContractId}};
            string[] names;
            if(refund==null)names=new[]{"vnp_RequestId","vnp_Version","vnp_Command","vnp_TmnCode","vnp_TxnRef","vnp_TransactionDate","vnp_CreateDate","vnp_IpAddr","vnp_OrderInfo"};
            else
            {
                fields.Add("vnp_TransactionType",refund.Amount==payment.Amount?"02":"03");fields.Add("vnp_Amount",(refund.Amount*100).ToString("0",CultureInfo.InvariantCulture));fields.Add("vnp_CreateBy","admin"+refund.RequestedById);
                names=new[]{"vnp_RequestId","vnp_Version","vnp_Command","vnp_TmnCode","vnp_TransactionType","vnp_TxnRef","vnp_Amount","vnp_TransactionNo","vnp_TransactionDate","vnp_CreateBy","vnp_CreateDate","vnp_IpAddr","vnp_OrderInfo"};
            }
            fields.Add("vnp_SecureHash",VnPayCodec.Hmac(settings.Secret,string.Join("|",names.Select(x=>fields[x]))));
            var handler=testTransport??new HttpClientHandler {AllowAutoRedirect=false};
            using(var deadline=new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(15)))
            using(var client=new HttpClient(handler,testTransport==null) {Timeout=TimeSpan.FromSeconds(15)})
            using(var request=new HttpRequestMessage(HttpMethod.Post,settings.ApiEndpoint) {Content=new StringContent(new JavaScriptSerializer().Serialize(fields),Encoding.UTF8,"application/json")})
            using(var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token))
            {
                if(!response.IsSuccessStatusCode || response.Content.Headers.ContentLength>65536)throw new InvalidOperationException("VNPay chưa trả kết quả hợp lệ; cần đối soát.");
                string json;
                using(var stream=await response.Content.ReadAsStreamAsync())
                using(var memory=new System.IO.MemoryStream())
                {
                    var block=new byte[4096];int read;
                    while((read=await stream.ReadAsync(block,0,block.Length,deadline.Token))>0) {if(memory.Length+read>65536)throw new InvalidOperationException("Phản hồi PSP quá lớn.");memory.Write(block,0,read);}
                    json=Encoding.UTF8.GetString(memory.ToArray());
                }
                var raw=new JavaScriptSerializer {MaxJsonLength=65536}.Deserialize<Dictionary<string,object>>(json);
                if(raw==null || raw.Count>35)throw new InvalidOperationException("Phản hồi PSP sai định dạng.");
                var result=new Dictionary<string,string>(StringComparer.Ordinal);
                foreach(var item in raw)
                {
                    if(item.Value!=null && !(item.Value is string) && !(item.Value is long) && !(item.Value is int))throw new InvalidOperationException("Phản hồi PSP chứa dữ liệu không hợp lệ.");
                    var value=Convert.ToString(item.Value,CultureInfo.InvariantCulture);if(value.Length>600)throw new InvalidOperationException("Phản hồi PSP vượt giới hạn.");result[item.Key]=value;
                }
                if(!VnPayCodec.VerifyApiResponse(result,settings))throw new InvalidOperationException("Không xác minh được chữ ký phản hồi PSP; cần đối soát.");
                return result;
            }
        }
    }
}
