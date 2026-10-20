using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace NexusFreelance.Web.Models
{
    // .NET Framework's UrlAttribute rejects valid HTTPS loopback links.
    // Keep its public-URL rules while supporting local case-study artifacts.
    public sealed class PortfolioUrlAttribute : ValidationAttribute
    {
        public PortfolioUrlAttribute() : base("Nhập URL đầy đủ, không chứa tên đăng nhập hoặc mật khẩu.") { }
        public override bool IsValid(object value)
        {
            if(value==null || string.Equals(value as string,"",StringComparison.Ordinal)) return true;
            var text=value as string;
            Uri uri;
            if(text==null || text.Any(char.IsWhiteSpace) || text.Contains("\\") ||
                !Uri.TryCreate(text,UriKind.Absolute,out uri) || !string.IsNullOrEmpty(uri.UserInfo)) return false;
            if(new UrlAttribute().IsValid(text)) return true;
            return uri.Scheme==Uri.UriSchemeHttps && uri.IsLoopback;
        }
    }
}
