using System;
namespace NexusFreelance.Web.Data
{
    internal static class ContractSchemaSeed
    {
        internal static void Apply(AppDbContext db)
        {
            // Fixed application-owned DDL only. Automatic migration is a development bootstrap.
            Action<string,string,string> check=(name,table,rule)=>db.Database.ExecuteSqlCommand("IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name='"+name+"') ALTER TABLE dbo.["+table+"] ADD CONSTRAINT ["+name+"] CHECK ("+rule+")");
            check("CK_Contract_Source","Contracts","(ProposalId IS NOT NULL AND GroupBidId IS NULL) OR (ProposalId IS NULL AND GroupBidId IS NOT NULL)");
            check("CK_Contract_State","Contracts","CurrentVersion>=1 AND Status IN ('AwaitingSignatures','AwaitingEscrow','Active','Completed','Disputed','Declined','Terminated')");
            check("CK_ContractVersion_Value","ContractVersions","VersionNumber>=1 AND Price>0 AND Price=FLOOR(Price) AND FeeRate BETWEEN 0 AND 1 AND DeliveryDays BETWEEN 1 AND 3650 AND LEN(Hash)=64");
            check("CK_ContractMember_Weight","ContractMembers","Weight>0 AND Weight=FLOOR(Weight)");
            check("CK_ContractAcceptance_Hash","ContractAcceptances","LEN(AcceptedHash)=64");
            check("CK_PaymentIntent_State","PaymentIntents","Amount>0 AND Amount=FLOOR(Amount) AND Currency='VND' AND Environment IN ('sandbox','production') AND Status IN ('Pending','Paid','PaidUnallocated','Failed') AND ExpiresAtUtc>CreatedAtUtc");
            check("CK_PspRefund_State","PspRefunds","Amount>0 AND Amount=FLOOR(Amount) AND Status IN ('Queued','Processing','Unknown','Succeeded','Failed')");
            db.Database.ExecuteSqlCommand(@"IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='UX_Workspace_Contract' AND object_id=OBJECT_ID('dbo.Workspaces')) CREATE UNIQUE INDEX UX_Workspace_Contract ON dbo.Workspaces(ContractId) WHERE ContractId IS NOT NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='UX_Share_ContractUser' AND object_id=OBJECT_ID('dbo.EscrowShares')) CREATE UNIQUE INDEX UX_Share_ContractUser ON dbo.EscrowShares(ContractId,UserAccountId) WHERE ContractId IS NOT NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='UX_Share_LegacyProjectUser' AND object_id=OBJECT_ID('dbo.EscrowShares')) CREATE UNIQUE INDEX UX_Share_LegacyProjectUser ON dbo.EscrowShares(ProjectId,UserAccountId) WHERE ContractId IS NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='UX_PaymentIntent_Pending' AND object_id=OBJECT_ID('dbo.PaymentIntents')) CREATE UNIQUE INDEX UX_PaymentIntent_Pending ON dbo.PaymentIntents(EscrowMilestoneId) WHERE Status='Pending';
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='UX_PaymentIntent_ProviderTxn' AND object_id=OBJECT_ID('dbo.PaymentIntents')) CREATE UNIQUE INDEX UX_PaymentIntent_ProviderTxn ON dbo.PaymentIntents(Environment,MerchantCode,ProviderTransactionNo) WHERE ProviderTransactionNo IS NOT NULL;");
            foreach(var table in new[]{"ContractVersions","ContractAcceptances","ContractMembers"})
                db.Database.ExecuteSqlCommand("IF OBJECT_ID('dbo.TR_"+table+"_Immutable') IS NULL EXEC('CREATE TRIGGER dbo.TR_"+table+"_Immutable ON dbo.["+table+"] AFTER UPDATE AS BEGIN SET NOCOUNT ON; THROW 51600, ''Signed snapshots, roster and acceptance records are append-only'', 1; END')");
            db.Database.ExecuteSqlCommand(@"IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE name='CK_WalletEntry_Ledger' AND definition NOT LIKE '%PspCapture%') ALTER TABLE dbo.WalletEntries DROP CONSTRAINT CK_WalletEntry_Ledger;");
            check("CK_WalletEntry_Ledger","WalletEntries","(Kind IN ('Deposit','Refund','Payout','PspCapture','PspRefundRestore') AND BalanceDelta=Amount AND HeldDelta=0) OR (Kind IN ('Withdrawal','PspRefundReserve') AND BalanceDelta=-Amount AND HeldDelta=0) OR (Kind='EscrowHold' AND BalanceDelta=-Amount AND HeldDelta=Amount) OR (Kind='EscrowSettle' AND BalanceDelta=0 AND HeldDelta=-Amount) OR (Kind='PlatformFee' AND BalanceDelta=0 AND HeldDelta=0)");
        }
    }
}
