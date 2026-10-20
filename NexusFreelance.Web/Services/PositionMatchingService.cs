using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using NexusFreelance.Web.Data;
using NexusFreelance.Web.Models;
using NexusFreelance.Web.ViewModels;
namespace NexusFreelance.Web.Services
{
 public sealed class PositionMatchingService
 {
  private readonly AppDbContext db;
  public PositionMatchingService(AppDbContext context){db=context;}
  public List<FreelancerProfile> Eligible(Project project)=>db.FreelancerProfiles.AsNoTracking().Include(p=>p.User)
   .Where(p=>p.User.Role=="Freelancer" && !p.User.IsSuspended && p.User.EmailVerifiedAtUtc.HasValue && p.IsPublic!=false && p.IsAvailable && p.MinimumProjectBudget<=project.Budget && p.User.Email!=project.Client.Email && (project.Client.GoogleSubject==null || p.User.GoogleSubject!=project.Client.GoogleSubject))
   .OrderByDescending(p=>p.UpdatedAtUtc).Take(500).ToList();
  public static MatchCard Score(RoleRequirement role,Project project,IList<ProjectSkillRequirement> required,FreelancerProfile profile,IList<VerifiedSkillRow> evidence,IList<VerifiedSkillRow> selected) {
   var card=new MatchCard {Id=profile.UserAccountId,Kind="freelancer",Name=profile.User.Name,Subtitle=profile.CoreRole,Required=required.Count};
   foreach(var skill in required){var held=evidence.Where(e=>e.SkillId==skill.SkillTrackId && e.Level>=skill.MinimumLevel).OrderByDescending(e=>e.Level).FirstOrDefault();if(held!=null){card.Covered++;card.Reasons.Add(held.Title+" · "+held.Level+"/5 · "+held.Evidence);}else card.Reasons.Add("Còn thiếu: "+skill.Skill.Title+" từ mức "+skill.MinimumLevel+"/5");}
   if(required.Count==0){card.Reasons.Add("Chưa ánh xạ được kỹ năng cho vị trí; cần Client làm rõ trước khi đánh giá.");return card;}
   card.Score=(int)Math.Round(80.0*card.Covered/required.Count);
   if(card.Covered>0 && selected.Any(r=>required.Any(s=>s.SkillTrackId==r.SkillId)&&evidence.Any(e=>e.SkillId==r.SkillId&&e.Level>=required.First(s=>s.SkillTrackId==r.SkillId).MinimumLevel))){card.Score+=10;card.Reasons.Add("Vai trò chính/phụ còn minh chứng phù hợp vị trí "+role.Title);}
   if(card.Covered>0 && profile.Industry==project.Industry)card.Score+=10;
   return card;
  }
  public List<PositionRecommendation> ForProject(Project project, int candidateLimit = 6) {
   var tracks=db.SkillTracks.AsNoTracking().Where(t=>t.IsActive!=false).ToList();var skills=db.ProjectSkillRequirements.Include(s=>s.Skill).Where(s=>s.ProjectId==project.Id).ToList();
   var profiles=Eligible(project);var ids=profiles.Select(p=>p.UserAccountId).ToArray();var matcher=new SmartMatchingService(db);var evidence=matcher.Evidence(ids);var selected=matcher.SelectedRoles(profiles,evidence);
   var assignments=db.RoleAssignments.Where(a=>a.ProjectId==project.Id).Select(a=>new{a.RoleRequirementId,a.FreelancerId}).ToList();
   return project.Requirements.OrderBy(r=>r.Id).Select(role=>{
    var required=RoleSkillResolver.ForRole(role,tracks,skills);var filled=assignments.Count(a=>a.RoleRequirementId==role.Id);
    return new PositionRecommendation {RoleId=role.Id,Title=role.Title,RequiredCount=role.RequiredCount,FilledCount=filled,Remaining=Math.Max(0,role.RequiredCount-filled),Skills=required,
     People=filled>=role.RequiredCount?new List<MatchCard>():profiles.Where(p=>!assignments.Any(a=>a.FreelancerId==p.UserAccountId)).Select(p=>Score(role,project,required,p,evidence[p.UserAccountId],selected[p.UserAccountId])).Where(c=>c.Covered>0).OrderByDescending(c=>c.Score).ThenBy(c=>c.Id).Take(Math.Max(1,Math.Min(500,candidateLimit))).ToList()};
   }).ToList();
  }
  // Maximum matching: an active person covers at most one hiring slot, even with multiple skills.
  public static Dictionary<int,int> AssignSlots(IList<PositionRecommendation> positions,IDictionary<int,List<VerifiedSkillRow>> members,IEnumerable<int> preferredMembers = null) {
   var slots=positions.SelectMany(p=>Enumerable.Range(0,Math.Max(0,p.Remaining)).Select(i=>p)).ToList();var assigned=new Dictionary<int,int>();
   var preferred=new HashSet<int>(preferredMembers??Enumerable.Empty<int>());var ordered=members.OrderBy(m=>preferred.Contains(m.Key)?0:1).ThenBy(m=>m.Key).ToArray();
   Func<int,HashSet<int>,bool> visit=null;
   visit=(slot,seen)=>{var p=slots[slot];if(p.Skills.Count==0)return false;foreach(var person in ordered){
    if(!p.Skills.All(s=>person.Value.Any(e=>e.SkillId==s.SkillTrackId && e.Level>=s.MinimumLevel)) || !seen.Add(person.Key))continue;
    int old;if(!assigned.TryGetValue(person.Key,out old)||visit(old,seen)){assigned[person.Key]=slot;return true;}
   }return false;};
   for(int i=0;i<slots.Count;i++)visit(i,new HashSet<int>());
   return assigned.ToDictionary(a=>a.Key,a=>slots[a.Value].RoleId);
  }
 }
}
