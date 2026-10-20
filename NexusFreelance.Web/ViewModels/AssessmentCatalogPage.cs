using System.Collections.Generic;
using NexusFreelance.Web.Services;
namespace NexusFreelance.Web.ViewModels
{
    public sealed class AssessmentCatalogPage
    {
        public string Query { get; set; }
        public string Industry { get; set; }
        public string Provider { get; set; }
        public List<AssessmentResource> Resources { get; set; }
    }
}
