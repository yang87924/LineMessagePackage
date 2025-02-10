using LineMessage.Enum;

namespace LineMessage.Dtos.Messages;

public class StickerMessageDto : BaseMessageDto
{
    public StickerMessageDto()
    {
        Type = MessageTypeEnum.Sticker;
    }
    public string PackageId { get; set; }
    public string StickerId { get; set; }
}