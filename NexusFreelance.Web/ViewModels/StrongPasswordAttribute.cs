using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace NexusFreelance.Web.ViewModels
{
    public sealed class StrongPasswordAttribute : ValidationAttribute
    {
        public StrongPasswordAttribute() { ErrorMessage = "Mật khẩu cần 12–128 ký tự, có chữ hoa, chữ thường, số và ký tự đặc biệt; tránh mật khẩu phổ biến."; }
        public override bool IsValid(object value)
        {
            var password = value as string;
            if (password == null) return true; // Required handles missing input.
            var common = new[] { "password", "123456789", "qwerty", "matkhau", "admin123", "letmein" };
            return password.Length >= 12 && password.Length <= 128 &&
                password.Any(char.IsUpper) && password.Any(char.IsLower) && password.Any(char.IsDigit) &&
                password.Any(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c)) &&
                !common.Any(s => password.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
