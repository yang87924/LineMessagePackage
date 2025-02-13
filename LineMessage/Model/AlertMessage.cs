using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LineMessage.Model
{
    public class AlertMessage
    {
        public DateTime SendTime { get; set; }
        public string MachineName { get; set; }
        public string ErrorMessage { get; set; }
        public string MachineData { get; set; }

        public string ToFormattedString()
        {
            return $"警告通知\n" +
                   $"發送時間: {SendTime:yyyy-MM-dd HH:mm:ss}\n" +
                   $"機台名稱: {MachineName}\n" +
                   $"異常訊息: {ErrorMessage}\n" +
                   $"機台數據: {MachineData}";
        }
    }

    public class LineSettings
    {
        [Key]
        public int Id { get; set; } // 新增這行

        public string ChannelAccessToken { get; set; }
        public string ChannelSecret { get; set; }
    }
}
