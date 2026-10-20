namespace NexusFreelance.Web.ViewModels
{
    public sealed class MilestoneEditor
    {
        public string Id {get;set;}
        public string FieldName {get;set;}="MilestonePlan";
        public string Plan {get;set;}
        public bool IsGroup {get;set;}
        public string TotalSelector {get;set;}
        public string DeliverySelector {get;set;}
        public decimal? FixedTotal {get;set;}
    }
}
