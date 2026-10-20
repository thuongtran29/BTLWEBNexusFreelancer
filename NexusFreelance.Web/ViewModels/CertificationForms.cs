using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using NexusFreelance.Web.Models;

namespace NexusFreelance.Web.ViewModels
{
    public sealed class SubmitCertificationForm
    {
        public int? SkillTrackId { get; set; }
        [Required, StringLength(180)] public string Name { get; set; }
        [Required, StringLength(160)] public string Issuer { get; set; }
        [Required, StringLength(1000), Url] public string EvidenceUrl { get; set; }
        [Required, DataType(DataType.Date)] public DateTime? IssuedOn { get; set; }
        [DataType(DataType.Date)] public DateTime? ExpiresOn { get; set; }
    }

    public sealed class CertificationReviewForm
    {
        public int? SkillTrackId { get; set; }
        [Range(1,5)] public int ProficiencyLevel { get; set; } = 1;
        [Range(1, int.MaxValue)] public int Id { get; set; }
        [Required] public string Decision { get; set; }
        [Required, StringLength(500, MinimumLength = 5)] public string Reason { get; set; }
    }

    public sealed class CertificationAdminPage
    {
        public string Status { get; set; }
        public int Page { get; set; }
        public int PageCount { get; set; }
        public int Total { get; set; }
        public IList<ExternalCertification> Certifications { get; set; } = new List<ExternalCertification>();
    }

    public sealed class MyCertificationsPage
    {
        public SubmitCertificationForm NewCertificate { get; set; } = new SubmitCertificationForm();
        public IList<ExternalCertification> Certifications { get; set; } = new List<ExternalCertification>();
    }
}
