using System.Collections.Generic;
using NexusFreelance.Web.Models;
namespace NexusFreelance.Web.ViewModels
{
 public class PositionRecommendation
 {
  public int RoleId {get;set;} public string Title {get;set;}
  public int RequiredCount {get;set;} public int FilledCount {get;set;}
  public int Remaining {get;set;} public int AwaitingCount {get;set;}
  public List<ProjectSkillRequirement> Skills {get;set;}=new List<ProjectSkillRequirement>();
  public List<MatchCard> People {get;set;}=new List<MatchCard>();
 }
 public class GroupStaffingPage
 {
  public FreelancerGroup Group {get;set;} public Project Project {get;set;}
  public GroupProjectRecruitment Recruitment {get;set;}
  public bool IsOpen {get;set;} public bool IsLeader {get;set;}
  public int MemberCount {get;set;} public bool CanRecruit {get;set;} public string AvailabilityNote {get;set;}
  public List<PositionRecommendation> Positions {get;set;}=new List<PositionRecommendation>();
  public List<PendingStaffInvitation> PendingInvitations {get;set;}=new List<PendingStaffInvitation>();
  public List<GroupJoinRequest> Requests {get;set;}=new List<GroupJoinRequest>();
 }
 public class PendingStaffInvitation { public int UserId {get;set;} public string Name {get;set;} }
 public class GroupOpportunity
 {
  public int RecruitmentId {get;set;} public int GroupId {get;set;}
  public string GroupName {get;set;} public string LeaderName {get;set;}
  public int RoleId {get;set;} public string RoleTitle {get;set;}
  public int Missing {get;set;} public int Score {get;set;}
  public string RequestStatus {get;set;}
  public List<string> Reasons {get;set;}=new List<string>();
 }
}
