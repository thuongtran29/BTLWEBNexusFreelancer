using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
namespace NexusFreelance.Web.ViewModels
{
 public class KnowledgePublishForm
 {
  public int ProjectId {get;set;} public int ExpectedVersion {get;set;}
  [Required,StringLength(180,MinimumLength=10)] public string Title {get;set;}
  [Required,StringLength(800,MinimumLength=30)] public string Summary {get;set;}
  [Required,StringLength(1500,MinimumLength=30)] public string Practices {get;set;}
  [Required,StringLength(1200,MinimumLength=30)] public string AcceptanceChecklist {get;set;}
  [StringLength(400)] public string SkillTags {get;set;}
  public bool ConfirmGeneric {get;set;}
 }
 // Deliberately excludes project title, author, contract, money, files and private memory.
 public class SharedKnowledgeCard
 {
  public int Id {get;set;} public int Version {get;set;}
  public string Title {get;set;} public string Industry {get;set;}
  public string Summary {get;set;} public string Practices {get;set;}
  public string AcceptanceChecklist {get;set;} public string SkillTags {get;set;}
  public DateTime CreatedAtUtc {get;set;}
 }
 public class KnowledgeLibraryPage
 {
  public string Query {get;set;} public string Industry {get;set;}
  public int Page {get;set;} public int Total {get;set;}
  public List<SharedKnowledgeCard> Items {get;set;}=new List<SharedKnowledgeCard>();
 }
 public class CompletedKnowledgeProject
 {
  public int Id {get;set;} public string Title {get;set;} public string Industry {get;set;}
  public bool IsOwner {get;set;} public int MemoryVersions {get;set;} public bool HasSharedArticle {get;set;}
 }
}
