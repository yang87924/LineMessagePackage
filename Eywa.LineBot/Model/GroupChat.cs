using System.ComponentModel.DataAnnotations;

namespace Eywa.LineBot.Model;

public class GroupChat
{
    [Key]
    public string GroupId { get; set; }
    public string GroupName { get; set; }
    public DateTime JoinedAt { get; set; }
    public bool IsActive { get; set; }
}