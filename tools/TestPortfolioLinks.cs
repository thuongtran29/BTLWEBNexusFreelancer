using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using NexusFreelance.Web.Models;
using NexusFreelance.Web.ViewModels;
class TestPortfolioLinks
{
    static int failures;
    static void Check(object model,string url,bool expected,string name)
    {
        model.GetType().GetProperty("Url").SetValue(model,url,null);
        var ok=Validator.TryValidateProperty(url,new ValidationContext(model){MemberName="Url"},new List<ValidationResult>());
        Console.WriteLine((ok==expected?"PASS ":"FAIL ")+name);if(ok!=expected)failures++;
    }
    static int Main()
    {
        foreach(var model in new object[]{new PortfolioItem(),new PortfolioItemForm()}) {
            var name=model.GetType().Name+" ";
            Check(model,"https://localhost:44391/Content/demo/portfolio.html#minh.frontend",true,name+"local HTTPS case study");
            Check(model,"https://example.com/portfolio",true,name+"public HTTPS");
            Check(model,"javascript:alert(1)",false,name+"reject script scheme");
            Check(model,"https://user:password@example.com/",false,name+"reject embedded credentials");
            Check(model,"/relative/portfolio",false,name+"reject relative URL");
        }
        return failures==0?0:1;
    }
}
