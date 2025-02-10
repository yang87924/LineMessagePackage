using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LineMessage.Dtos.webhook
{
    public class WebhookRequestBodyDto
    {
        public string? Destination { get; set; }
        public List<WebhookEventDto> Events { get; set; }
    }
}
