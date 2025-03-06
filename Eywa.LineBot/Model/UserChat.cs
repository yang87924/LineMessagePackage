using System.ComponentModel.DataAnnotations;

namespace Eywa.LineBot.Model;

public class UserChat
{
    [Key]
    public string UserId { get; set; }
    public string UserName { get; set; }
    public DateTime JoinedAt { get; set; }
    public bool IsActive { get; set; }
}