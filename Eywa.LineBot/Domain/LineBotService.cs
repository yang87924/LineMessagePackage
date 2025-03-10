using System.Net.Http.Headers;
using System.Text;
using Eywa.LineBot.Data;
using Eywa.LineBot.Dtos.Messages;
using Eywa.LineBot.Dtos.Messages.Request;
using Eywa.LineBot.Dtos.webhook;
using Eywa.LineBot.Enum;
using Eywa.LineBot.Model;
using Eywa.LineBot.Provider;
using LineMessage.Dtos.Messages;
using Microsoft.EntityFrameworkCore;

namespace Eywa.LineBot.Domain;

public interface ILineBotService
{
    Task StoreLineSettingsAsync(string channelAccessToken, string channelSecret);
    Task ReceiveWebhook(WebhookRequestBodyDto requestBody);
    Task SendGroupMessageHandlerAsync<T>(string groupId, BroadcastMessageRequestDto<T> requestBody) where T : BaseMessageDto;
    Task SendMessageToAllGroupsHandlerAsync<T>(BroadcastMessageRequestDto<T> requestBody) where T : BaseMessageDto;
    Task BroadcastMessageHandlerAsync<T>(BroadcastMessageRequestDto<T> requestBody) where T : BaseMessageDto;

    // Task<List<string>> GetActiveGroupIdsAsync();
    // Task<HttpResponseMessage> GetGroupSummaryAsync(string groupId);
    // Task<bool> IsUserActiveAsync(string userId);
    // Task ReplyMessageAsync<T>(string messageType, ReplyMessageRequestDto<T> request);
    // Task<LineSettings> GetLineSettingsAsync();
}

public class LineBotService : ILineBotService
{
    private readonly LineBotDbContext _dbContext;
    private readonly HttpClient _httpClient;
    private readonly IJsonProvider _jsonProvider;
    private string ChannelAccessToken { get; set; }
    private string ChannelSecret { get; set; }


    public LineBotService(LineBotDbContext dbContext, IJsonProvider jsonProvider)
    {
        _dbContext = dbContext;
        _httpClient = new HttpClient();
        _jsonProvider = jsonProvider;
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _dbContext.Database.EnsureCreated();
    }

    public async Task StoreLineSettingsAsync(string channelAccessToken, string channelSecret)
    {
        ChannelAccessToken = channelAccessToken;
        ChannelSecret = channelSecret;
    }
    public async Task BroadcastMessageHandlerAsync<T>(BroadcastMessageRequestDto<T> requestBody) where T : BaseMessageDto
    {
        await BroadcastMessageAsync(requestBody);
    }

    public async Task SendMessageToAllGroupsHandlerAsync<T>(BroadcastMessageRequestDto<T> requestBody) where T : BaseMessageDto
    {
        await SendMessageToAllGroupsAsync(requestBody);
    }
    public async Task SendGroupMessageHandlerAsync<T>(string groupId, BroadcastMessageRequestDto<T> requestBody) where T : BaseMessageDto
    {
        await SendGroupMessageAsync(groupId, requestBody);
    }
    private async Task BroadcastMessageAsync<T>(BroadcastMessageRequestDto<T> request)
    {
        var json = _jsonProvider.Serialize(request);
        var url = "https://api.line.me/v2/bot/message/broadcast";

        var response = await PostJson(url, json);
        var responseContent = await response.Content.ReadAsStringAsync();
    }

    private async Task<bool> SendGroupMessageAsync<T>(string groupId, BroadcastMessageRequestDto<T> request)
    {
        try
        {
            string pushMessageUri = "https://api.line.me/v2/bot/message/push";

            var pushRequest = new
            {
                to = groupId,
                messages = request.Messages
            };
            var json = _jsonProvider.Serialize(pushRequest);

            var response = await PostJson(pushMessageUri, json);
            var responseContent = await response.Content.ReadAsStringAsync();

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            return false;
        }
    }


    private async Task SendMessageToAllGroupsAsync<T>(BroadcastMessageRequestDto<T> request)
    {
        var activeGroups = await GetActiveGroupIdsAsync();
        foreach (var groupId in activeGroups)
        {
            await SendGroupMessageAsync(groupId, request);
        }
    }

    public async Task<List<string>> GetActiveGroupIdsAsync()
    {
        return await _dbContext.GroupChats
            .Where(g => g.IsActive)
            .Select(g => g.GroupId)
            .ToListAsync();
    }

    /// <summary>
    /// 獲取群聊摘要
    /// </summary>
    /// <param name="groupId"></param>
    /// <returns></returns>
    public async Task<HttpResponseMessage> GetGroupSummaryAsync(string groupId)
    {
        try
        {
            var requestUri = $"https://api.line.me/v2/bot/group/{groupId}/summary";
            var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ChannelAccessToken);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Status Code: {response.StatusCode}");
                Console.WriteLine($"Error Response: {errorContent}");
                Console.WriteLine($"Request URL: {requestUri}");
                Console.WriteLine($"Token: {ChannelAccessToken}");
            }

            return response;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception in GetGroupSummaryAsync: {ex.Message}");
            throw;
        }
    }

    public async Task ReceiveWebhook(WebhookRequestBodyDto requestBody)
    {
        foreach (var eventObject in requestBody.Events)
        {
            switch (eventObject.Type)
            {
                case WebhookEventTypeEnum.Join:
                    //Console.WriteLine($"Bot joined group: {eventObject.Source.GroupId}");
                    await SaveGroupAsync(eventObject.Source.GroupId, "Group " + eventObject.Source.GroupId);

                    var testMessage = new BroadcastMessageRequestDto<TextMessageDto>
                    {
                        Messages = new List<TextMessageDto>
                        {
                            new TextMessageDto { Text = "機器人已加入群組，這是測試訊息" }
                        }
                    };
                    await SendGroupMessageAsync(eventObject.Source.GroupId, testMessage);
                    break;

                case WebhookEventTypeEnum.Message:
                    if (eventObject.Source.Type == "group")
                    {
                        await SaveGroupAsync(eventObject.Source.GroupId, "Group " + eventObject.Source.GroupId);
                    }

                    if (eventObject.Message.Text.Equals("客服"))
                    {
                        var replyMessage = new ReplyMessageRequestDto<TextMessageDto>
                        {
                            ReplyToken = eventObject.ReplyToken,
                            Messages = new List<TextMessageDto>
                            {
                                new TextMessageDto { Text = $"您好，您傳送了\"{eventObject.Message.Text}\"!" }
                            }
                        };
                        await ReplyMessageAsync("text", replyMessage);
                    }
                    break;

                case WebhookEventTypeEnum.Unfollow:
                    var user = await _dbContext.UserChats.FindAsync(eventObject.Source.UserId);
                    if (user != null)
                    {
                        user.IsActive = false;
                        await _dbContext.SaveChangesAsync();
                    }
                    break;
            }
        }
    }
    private async Task SaveGroupAsync(string groupId, string groupName)
    {
        var existingGroup = await _dbContext.GroupChats.FindAsync(groupId);
        if (existingGroup == null)
        {
            await _dbContext.GroupChats.AddAsync(new GroupChat
            {
                GroupId = groupId,
                GroupName = groupName,
                JoinedAt = DateTime.UtcNow,
                IsActive = true
            });
        }
        else
        {
            existingGroup.IsActive = true;
            existingGroup.GroupName = groupName;
            _dbContext.GroupChats.Update(existingGroup);
        }
        await _dbContext.SaveChangesAsync();
    }

    public async Task ReplyMessageAsync<T>(string messageType, ReplyMessageRequestDto<T> request)
    {
        var json = _jsonProvider.Serialize(request);
        var url = "https://api.line.me/v2/bot/message/reply";
        PostJson(url, json);
    }

    private async Task<HttpResponseMessage> PostJson(string url, string json)
    {
        try
        {
            //_httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ChannelAccessToken);
            var requestMessage = new HttpRequestMessage
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri(url),
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            var response = await _httpClient.SendAsync(requestMessage);
            return response;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception in PostJson: {ex.Message}");
            throw;
        }
        
        
    }
}