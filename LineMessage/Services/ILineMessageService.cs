using LineMessage.Model;
using Microsoft.Extensions.Logging;

namespace LineMessage.Services;
public interface ILineMessageService
{
   Task<bool> SendAlertMessageAsync(string targetId, string machineName, string errorMessage, string machineData);
   Task<bool> SendAlertMessageAsync(string targetId, AlertMessage message);
}
public class LineMessageService : ILineMessageService
{
   private readonly isRock.LineBot.Bot _lineBot;
   private readonly ILogger<LineMessageService> _logger;

   public LineMessageService(LineSettings settings, ILogger<LineMessageService> logger)
   {
      _lineBot = new isRock.LineBot.Bot(settings.ChannelAccessToken);
      _logger = logger;
   }

   public async Task<bool> SendAlertMessageAsync(string targetId, string machineName, string errorMessage, string machineData)
   {
      var message = new AlertMessage
      {
         SendTime = DateTime.Now,
         MachineName = machineName,
         ErrorMessage = errorMessage,
         MachineData = machineData
      };

      return await SendAlertMessageAsync(targetId, message);
   }

   public async Task<bool> SendAlertMessageAsync(string targetId, AlertMessage message)
   {
      try
      {
         _logger.LogInformation($"開始發送警告訊息給 {targetId}");

         // 使用 David Tung 的 LineBotSDK 發送訊息
         await Task.Run(() => _lineBot.PushMessage(targetId, message.ToFormattedString()));

         _logger.LogInformation($"成功發送警告訊息給 {targetId}");
         return true;
      }
      catch (Exception ex)
      {
         _logger.LogError(ex, $"發送警告訊息給 {targetId} 時發生錯誤");
         throw;
      }
   }
}