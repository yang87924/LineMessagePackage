using System.Net.Http.Headers;
using System.Text;
using LineMessage.Data;
using LineMessage.Dtos;
using LineMessage.Dtos.Messages;
using LineMessage.Dtos.Messages.Request;
using LineMessage.Dtos.webhook;
using LineMessage.Enum;
using LineMessage.Model;
using LineMessage.Provider;
using Microsoft.EntityFrameworkCore;

namespace LineMessage.Domain;
public interface ILineBotService
{
    Task BroadcastMessageHandlerAsync(string messageType, object requestBody);
    Task BroadcastMessageAsync<T>(BroadcastMessageRequestDto<T> request);
    Task SaveGroupAsync(string groupId, string groupName);
    Task DeactivateGroupAsync(string groupId);
    Task<List<string>> GetActiveGroupIdsAsync();
    Task<HttpResponseMessage> GetGroupSummaryAsync(string groupId);
    Task ReceiveWebhook(WebhookRequestBodyDto requestBody);
    Task<bool> IsUserActiveAsync(string userId);
    Task<bool> SendGroupMessageAsync<T>(string groupId, BroadcastMessageRequestDto<T> request);
    Task SendMessageToAllGroupsAsync<T>(BroadcastMessageRequestDto<T> request);
    Task ReplyMessageAsync<T>(string messageType, ReplyMessageRequestDto<T> request);
    Task StoreLineSettingsAsync(string channelAccessToken, string channelSecret);
    Task<LineSettings> GetLineSettingsAsync();
}

public class LineBotService : ILineBotService
{
    private readonly LineBotDbContext _dbContext;
    private readonly HttpClient _httpClient;
    private readonly JsonProvider _jsonProvider;

    public LineBotService(LineBotDbContext dbContext, HttpClient httpClient, JsonProvider jsonProvider)
    {
        _dbContext = dbContext;
        _httpClient = httpClient;
        _jsonProvider = jsonProvider;
        _dbContext.Database.EnsureCreated();
    }

    public async Task StoreLineSettingsAsync(string channelAccessToken, string channelSecret)
    {
        var settings = await _dbContext.LineSettings.FirstOrDefaultAsync();
        if (settings == null)
        {
            settings = new LineSettings
            {
                ChannelAccessToken = channelAccessToken,
                ChannelSecret = channelSecret
            };
            _dbContext.LineSettings.Add(settings);
        }
        else
        {
            settings.ChannelAccessToken = channelAccessToken;
            settings.ChannelSecret = channelSecret;
            _dbContext.LineSettings.Update(settings);
        }
        await _dbContext.SaveChangesAsync();
    }

    public async Task<LineSettings> GetLineSettingsAsync()
    {
        return await _dbContext.LineSettings.FirstOrDefaultAsync();
    }

    public async Task BroadcastMessageHandlerAsync(string messageType, object requestBody)
    {
        string strBody = requestBody.ToString();
        switch (messageType)
        {
            case MessageTypeEnum.Text:
                var messageRequest = _jsonProvider.Deserialize<BroadcastMessageRequestDto<TextMessageDto>>(strBody);
                await BroadcastMessageAsync(messageRequest);
                break;
        }
    }

    public async Task BroadcastMessageAsync<T>(BroadcastMessageRequestDto<T> request)
    {
        var settings = await GetLineSettingsAsync();
        if (settings == null)
        {
            throw new InvalidOperationException("Line settings not configured.");
        }

        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ChannelAccessToken);
        var json = _jsonProvider.Serialize(request);
        var requestMessage = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            RequestUri = new Uri("https://api.line.me/v2/bot/message/broadcast"),
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        var response = await _httpClient.SendAsync(requestMessage);
        var responseContent = await response.Content.ReadAsStringAsync();
        Console.WriteLine(responseContent);
    }
    public async Task SaveGroupAsync(string groupId, string groupName)
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

    public async Task DeactivateGroupAsync(string groupId)
    {
        var group = await _dbContext.GroupChats.FindAsync(groupId);
        if (group != null)
        {
            group.IsActive = false;
            _dbContext.GroupChats.Update(group);
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task<List<string>> GetActiveGroupIdsAsync()
    {
        return await _dbContext.GroupChats
            .Where(g => g.IsActive)
            .Select(g => g.GroupId)
            .ToListAsync();
    }

    public async Task<HttpResponseMessage> GetGroupSummaryAsync(string groupId)
    {
        var settings = await GetLineSettingsAsync();
        if (settings == null)
        {
            throw new InvalidOperationException("Line settings not configured.");
        }

        try
        {
            var requestUri = $"https://api.line.me/v2/bot/group/{groupId}/summary";
            var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ChannelAccessToken);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Status Code: {response.StatusCode}");
                Console.WriteLine($"Error Response: {errorContent}");
                Console.WriteLine($"Request URL: {requestUri}");
                Console.WriteLine($"Token: {settings.ChannelAccessToken}");
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
                    Console.WriteLine($"Bot joined group: {eventObject.Source.GroupId}");
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

                    var replyMessage = new ReplyMessageRequestDto<TextMessageDto>
                    {
                        ReplyToken = eventObject.ReplyToken,
                        Messages = new List<TextMessageDto>
                        {
                            new TextMessageDto { Text = $"您好，您傳送了\"{eventObject.Message.Text}\"!" }
                        }
                    };
                    await ReplyMessageAsync("text", replyMessage);
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

    public async Task<bool> IsUserActiveAsync(string userId)
    {
        var user = await _dbContext.UserChats.FindAsync(userId);
        return user != null && user.IsActive;
    }

    public async Task<bool> SendGroupMessageAsync<T>(string groupId, BroadcastMessageRequestDto<T> request)
    {
        var settings = await GetLineSettingsAsync();
        if (settings == null)
        {
            throw new InvalidOperationException("Line settings not configured.");
        }

        try
        {
            string pushMessageUri = "https://api.line.me/v2/bot/message/push";

            var pushRequest = new
            {
                to = groupId,
                messages = request.Messages
            };

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ChannelAccessToken);

            var json = _jsonProvider.Serialize(pushRequest);
            Console.WriteLine($"Sending message to group {groupId}");
            Console.WriteLine($"Request body: {json}");

            var requestMessage = new HttpRequestMessage
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri(pushMessageUri),
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            var response = await _httpClient.SendAsync(requestMessage);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed to send message to group {groupId}. Status: {response.StatusCode}");
                Console.WriteLine($"Response: {responseContent}");
                return false;
            }

            Console.WriteLine($"Successfully sent message to group {groupId}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending message to group {groupId}: {ex.Message}");
            return false;
        }
    }

    public async Task SendMessageToAllGroupsAsync<T>(BroadcastMessageRequestDto<T> request)
    {
        var activeGroups = await GetActiveGroupIdsAsync();
        foreach (var groupId in activeGroups)
        {
            await SendGroupMessageAsync(groupId, request);
        }
    }

    public async Task ReplyMessageAsync<T>(string messageType, ReplyMessageRequestDto<T> request)
    {
        var settings = await GetLineSettingsAsync();
        if (settings == null)
        {
            throw new InvalidOperationException("Line settings not configured.");
        }

        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ChannelAccessToken);

        var json = _jsonProvider.Serialize(request);
        var requestMessage = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            RequestUri = new Uri("https://api.line.me/v2/bot/message/reply"),
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        var response = await _httpClient.SendAsync(requestMessage);
        Console.WriteLine(await response.Content.ReadAsStringAsync());
    }
}