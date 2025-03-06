namespace Eywa.LineBot.Dtos.Messages.Request;

public class BroadcastMessageRequestDto<T> : BaseMessageDto
{
    public List<T> Messages { get; set; }
    public bool? NotificationDisabled { get; set; }
}