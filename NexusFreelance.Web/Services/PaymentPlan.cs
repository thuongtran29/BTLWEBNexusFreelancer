using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Script.Serialization;
using NexusFreelance.Web.Models;
namespace NexusFreelance.Web.Services
{
    public static class PaymentPlan
    {
        public static List<EscrowMilestone> Preview(string text)
        {
            if(string.IsNullOrWhiteSpace(text))return new List<EscrowMilestone>();
            try {
                decimal total;
                if(text.TrimStart().StartsWith("[",StringComparison.Ordinal)){
                    var rows=ReadStructured(text);
                    if(rows==null || rows.Any(r=>r==null))return new List<EscrowMilestone>();
                    total=rows.Sum(r=>r.Amount);
                } else total=text.Split(new[]{'\n'},StringSplitOptions.RemoveEmptyEntries).Sum(line=>decimal.Parse(line.Split('|')[1].Trim(),NumberStyles.None,CultureInfo.InvariantCulture));
                return Parse(text,total,0);
            } catch(Exception ex) when(ex is ArgumentException || ex is InvalidOperationException || ex is FormatException || ex is OverflowException || ex is IndexOutOfRangeException){return new List<EscrowMilestone>();}
        }
        public sealed class Row
        {
            public string Title {get;set;} public decimal Amount {get;set;}
            public int? DueDay {get;set;} public string AcceptanceCriteria {get;set;}
        }
        private static decimal Number(object value)
        {
            decimal number;
            if(!(value is int || value is long || value is decimal || value is double) || !decimal.TryParse(Convert.ToString(value,CultureInfo.InvariantCulture),NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out number))
                throw new InvalidOperationException("Số tiền hoặc ngày hạn của mốc không hợp lệ.");
            return number;
        }
        private static List<Row> ReadStructured(string text)
        {
            var items=new JavaScriptSerializer{MaxJsonLength=4000}.DeserializeObject(text) as object[];
            if(items==null || items.Length<1 || items.Length>20)throw new InvalidOperationException("Cần từ 1 đến 20 mốc.");
            var result=new List<Row>();
            foreach(var item in items)
            {
                var map=item as Dictionary<string,object>;object title,amount,day,criteria;
                if(map==null || !map.TryGetValue("Title",out title) || !map.TryGetValue("Amount",out amount) || !map.TryGetValue("DueDay",out day) || !map.TryGetValue("AcceptanceCriteria",out criteria))
                    throw new InvalidOperationException("Mốc thiếu tên, số tiền, ngày hạn hoặc tiêu chí nghiệm thu.");
                var due=Number(day);
                if(due<1 || due>3650 || due!=decimal.Truncate(due))throw new InvalidOperationException("Ngày hạn phải là số nguyên từ 1 đến 3650.");
                result.Add(new Row{Title=title as string,Amount=Number(amount),DueDay=(int)due,AcceptanceCriteria=criteria as string});
            }
            return result;
        }
        public static List<EscrowMilestone> Parse(string text, decimal expectedTotal, int projectId) => Parse(text,expectedTotal,projectId,null);
        public static List<EscrowMilestone> Parse(string text, decimal expectedTotal, int projectId, int? deliveryDays)
        {
            if(!WalletService.ValidAmount(expectedTotal))throw new InvalidOperationException("Tổng giá phải là số nguyên VND lớn hơn 0.");
            if (string.IsNullOrWhiteSpace(text)) return new List<EscrowMilestone> { new EscrowMilestone { ProjectId = projectId, Title = "Bàn giao toàn bộ phạm vi đã thỏa thuận", Amount = expectedTotal, DueDay=deliveryDays, AcceptanceCriteria=deliveryDays.HasValue?"Hoàn thành phạm vi và sản phẩm bàn giao đã thống nhất trong hợp đồng.":null, Status = "Planned", CreatedAtUtc = DateTime.UtcNow } };
            if (text.Length > 4000) throw new InvalidOperationException("Kế hoạch mốc tối đa 4000 ký tự.");
            var result = new List<EscrowMilestone>();
            if(text.TrimStart().StartsWith("[",StringComparison.Ordinal))
            {
                List<Row> rows;
                try {rows=ReadStructured(text);}
                catch(Exception ex) when(ex is ArgumentException || ex is InvalidOperationException || ex is FormatException || ex is OverflowException){throw new InvalidOperationException("Kế hoạch mốc không hợp lệ. Hãy kiểm tra từng dòng.");}
                if(rows==null || rows.Count<1 || rows.Count>20)throw new InvalidOperationException("Cần từ 1 đến 20 mốc.");
                int previousDay=0;
                foreach(var row in rows)
                {
                    if(row==null || string.IsNullOrWhiteSpace(row.Title) || row.Title.Trim().Length<3 || row.Title.Trim().Length>180 || !WalletService.ValidAmount(row.Amount) ||
                        !row.DueDay.HasValue || row.DueDay<1 || row.DueDay>(deliveryDays??3650) || row.DueDay<previousDay ||
                        string.IsNullOrWhiteSpace(row.AcceptanceCriteria) || row.AcceptanceCriteria.Trim().Length<10 || row.AcceptanceCriteria.Trim().Length>1000)
                        throw new InvalidOperationException("Mỗi mốc cần tên 3–180 ký tự, tiền nguyên VND, ngày hạn theo thứ tự trong thời gian thực hiện, và tiêu chí nghiệm thu 10–1000 ký tự.");
                    previousDay=row.DueDay.Value;
                    result.Add(new EscrowMilestone{ProjectId=projectId,Title=row.Title.Trim(),Amount=row.Amount,DueDay=row.DueDay,AcceptanceCriteria=row.AcceptanceCriteria.Trim(),Status="Planned",CreatedAtUtc=DateTime.UtcNow});
                }
                if(result.Sum(x=>x.Amount)!=expectedTotal)throw new InvalidOperationException("Tổng tiền các mốc phải bằng đúng giá chào / giá hợp đồng.");
                return result;
            }
            foreach (var line in text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Trim().Split('|'); decimal amount;
                if (parts.Length != 2 || parts[0].Trim().Length < 3 || parts[0].Trim().Length > 180 ||
                    !decimal.TryParse(parts[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out amount) || !WalletService.ValidAmount(amount))
                    throw new InvalidOperationException("Mỗi dòng mốc phải có: Tiêu đề (3–180 ký tự) | Số tiền nguyên VND.");
                result.Add(new EscrowMilestone { ProjectId = projectId, Title = parts[0].Trim(), Amount = amount, Status = "Planned", CreatedAtUtc = DateTime.UtcNow });
            }
            if (result.Count < 1 || result.Count > 20 || result.Sum(x => x.Amount) != expectedTotal) throw new InvalidOperationException("Cần 1–20 mốc có tổng bằng tổng giá chào / giá hợp đồng.");
            return result;
        }
    }
}
