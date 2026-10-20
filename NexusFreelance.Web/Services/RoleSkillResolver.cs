using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NexusFreelance.Web.Models;
namespace NexusFreelance.Web.Services
{
 public static class RoleSkillResolver
 {
  public static string Normalize(string value) {
   var s=(value??"").ToLowerInvariant().Replace('đ','d').Normalize(NormalizationForm.FormD);
   s=new string(s.Where(c=>CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark).ToArray());
   return Regex.Replace(Regex.Replace(s,@"[^a-z0-9+#]+"," "),@"\s+"," ").Trim();
  }
  private static bool Has(string text,params string[] words)=>words.Any(w=>(" "+text+" ").Contains(" "+Normalize(w)+" "));
  public static HashSet<string> Categories(string value) {
   var text=Normalize(value);var result=new HashSet<string>();
   if(Has(text,"full stack","fullstack")){result.Add("backend");result.Add("frontend");}
   if(Has(text,"backend","back end","api","c#","asp net","dotnet","phia may chu"))result.Add("backend");
   if(Has(text,"frontend","front end","html","css","javascript","react"))result.Add("frontend");
   if(Has(text,"mobile","android","ios","di dong"))result.Add("mobile");
   if(Has(text,"ui","ux","figma","trai nghiem","prototype"))result.Add("uiux");
   if(Has(text,"graphic","branding","logo","do hoa","nhan dien"))result.Add("graphic");
   if(Has(text,"seo"))result.Add("seo");
   if(Has(text,"digital marketer","marketing","tiep thi","quang cao"))result.Add("marketing");
   if(Has(text,"content","writer","editor","noi dung","copywriting","bien tap"))result.Add("content");
   if(Has(text,"translator","translation","localization","bien dich","hieu dinh"))result.Add("translation");
   if(Has(text,"business","nghiep vu","consultant","tu van"))result.Add("business");
   if(Has(text,"project manager","project management","quan ly du an"))result.Add("project");
   if(Has(text,"data analyst","data analysis","data engineer","phan tich du lieu","xu ly du lieu"))result.Add("data");
   if(Has(text,"machine learning","ml","hoc may","ai engineer"))result.Add("ml");
   if(Has(text,"sql","database","co so du lieu"))result.Add("database");
   if(Has(text,"qa","tester","testing","kiem thu"))result.Add("qa");
   if(Has(text,"cad","ban ve","drafting"))result.Add("cad");
   if(Has(text,"bim","revit","thong tin cong trinh"))result.Add("bim");
   if(Has(text,"research","nghien cuu","tong hop thong tin"))result.Add("research");
   if(Has(text,"operations","dieu phoi"))result.Add("operations");
   return result;
  }
  public static List<ProjectSkillRequirement> ForRole(RoleRequirement role,IEnumerable<SkillTrack> tracks,IList<ProjectSkillRequirement> projectSkills) {
   var categories=Categories(role.Title);var selected=tracks.Where(t=>t.IsActive!=false).Where(t=>{
    var own=Categories(t.Title);if(own.Count==0)own=Categories(t.MatchKeywords);return own.Overlaps(categories);
   }).OrderBy(t=>t.Id).ToList();
   var explicitSkills=selected.Where(t=>projectSkills.Any(p=>p.SkillTrackId==t.Id)).ToList();if(explicitSkills.Count>0)selected=explicitSkills;
   return selected.Take(4).Select(t=>new ProjectSkillRequirement {ProjectId=role.ProjectId,SkillTrackId=t.Id,Skill=t,MinimumLevel=projectSkills.Where(p=>p.SkillTrackId==t.Id).Select(p=>p.MinimumLevel).DefaultIfEmpty(1).Max()}).ToList();
  }
 }
}
