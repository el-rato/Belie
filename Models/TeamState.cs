namespace WallpaperProfiles.Models;

/// <summary>The creature team that grows with the person's GitHub contributions.</summary>
public sealed class TeamState
{
    public List<TeamMember> Members { get; set; } = new();
    /// <summary>Index into <see cref="Members"/> of the one that walks the desktop.</summary>
    public int Leader { get; set; }
    /// <summary>Experience goes to everyone, or only to the leader.</summary>
    public bool ExpShare { get; set; } = true;
    public bool Walks { get; set; } = true;
    /// <summary>The contribution total at the last count; -1 before the first.</summary>
    public int LastTotal { get; set; } = -1;
    public int Streak { get; set; }
    public string Login { get; set; } = "";
    /// <summary>The GitHub account to count. Empty is whoever is signed in to the GitHub CLI.</summary>
    public string GitHubUser { get; set; } = "";
    public DateTime? CountedAt { get; set; }
    /// <summary>Where the creature pack comes from. Empty is Belie's own.</summary>
    public string PackUrl { get; set; } = "";
}

public sealed class TeamMember
{
    public string Id { get; set; } = "";
    public double Xp { get; set; }
}
