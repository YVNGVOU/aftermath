// ============================================================================
// SIMULATED DEMO DATA - NOT REAL. READ THIS BEFORE TOUCHING THIS FILE.
//
// Every value returned from this file is a hardcoded, made-up literal written
// for the Organization tab's product demonstration only. Aftermath is
// single-account today (see OwnerWorkspace.cs's header comment) - there is no
// real multi-user backend, no real invitation system, and no real role
// enforcement anywhere in this app. The user explicitly asked for this
// simulated view after being told it conflicts with the app's normal
// "never fabricate data" rule, and explicitly chose to proceed with clearly
// labeled fake data instead of skipping the feature.
//
// HARD RULE FOR ANYONE EDITING THIS FILE: no function here may ever read
// from disk (File.*), the registry (Registry.*), WMI/CIM (ManagementObject,
// ManagementObjectSearcher), environment/process info (Process.*,
// Environment.*), or any other real telemetry source. Every value returned
// must be an inline literal. If you find yourself wanting to derive a value
// from SweepAuditReader, LicenseStore, or anything else real, stop - that
// would silently blend fake and real data, which is exactly what the
// Organization tab's SIMULATED banner promises the user will never happen.
// ============================================================================

using System.Collections.Generic;

namespace Aftermath
{
    public class SimUser
    {
        public string Name;
        public string Role;
        public string Status;
        public string LastActivity;
    }

    public class SimRoleInfo
    {
        public string Role;
        public string Description;
    }

    public class SimInvitation
    {
        public string Email;
        public string InvitedBy;
        public string InvitedWhen;
    }

    public static class SimulatedOrgData
    {
        // Hardcoded demo roster. Not read from any account system - there
        // isn't one. Every field below is a literal typed in by hand.
        public static List<SimUser> Users()
        {
            return new List<SimUser>
            {
                new SimUser { Name = "Jordan Ellis",   Role = "Owner",         Status = "Active",  LastActivity = "2 hours ago" },
                new SimUser { Name = "Priya Nair",      Role = "Administrator", Status = "Active",  LastActivity = "1 day ago" },
                new SimUser { Name = "Marcus Webb",     Role = "Administrator", Status = "Active",  LastActivity = "3 days ago" },
                new SimUser { Name = "Sofia Torres",    Role = "Analyst",       Status = "Active",  LastActivity = "6 hours ago" },
                new SimUser { Name = "Daniel Kim",      Role = "Analyst",       Status = "Invited", LastActivity = "never" },
                new SimUser { Name = "Aaliyah Brooks",  Role = "Member",        Status = "Active",  LastActivity = "2 weeks ago" },
                new SimUser { Name = "Tomas Novak",     Role = "Member",        Status = "Invited", LastActivity = "never" },
            };
        }

        // Planned role model - a concept description, not a claim of enforced
        // permissions. Nothing in Aftermath currently checks these roles.
        public static List<SimRoleInfo> Roles()
        {
            return new List<SimRoleInfo>
            {
                new SimRoleInfo { Role = "Owner", Description = "Full access to billing, policies, and user management." },
                new SimRoleInfo { Role = "Administrator", Description = "Manage devices and policies. No billing access." },
                new SimRoleInfo { Role = "Analyst", Description = "View detections and warnings. No configuration access." },
                new SimRoleInfo { Role = "Member", Description = "View own device only." },
            };
        }

        // Hardcoded demo pending invitations. No real invitation ever exists.
        public static List<SimInvitation> Invitations()
        {
            return new List<SimInvitation>
            {
                new SimInvitation { Email = "daniel.kim@example.com",     InvitedBy = "Jordan Ellis", InvitedWhen = "3 days ago" },
                new SimInvitation { Email = "tomas.novak@example.com",    InvitedBy = "Priya Nair",   InvitedWhen = "1 week ago" },
                new SimInvitation { Email = "hana.suzuki@example.com",    InvitedBy = "Jordan Ellis", InvitedWhen = "2 weeks ago" },
            };
        }
    }
}
