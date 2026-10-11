using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Shared.Enums;

namespace ScheduleIsPlugin;

public class ScheduleHttpServer : IHostedService
{
    private readonly ILessonsService _lessonsService;
    private readonly ILogger<ScheduleHttpServer>? _logger;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private const int Port = 23850;

    public ScheduleHttpServer(ILessonsService lessonsService, ILogger<ScheduleHttpServer>? logger = null)
    {
        _lessonsService = lessonsService;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = new CancellationTokenSource();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();

        Task.Run(() => ListenLoopAsync(_cts.Token));
        _logger?.LogInformation("Schedule.is HTTP 服务已启动，监听端口: {Port}", Port);
        return Task.CompletedTask;
    }

    private async Task ListenLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener is { IsListening: true })
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = ProcessRequestAsync(context);
            }
            catch (HttpListenerException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Schedule.is 请求处理异常");
            }
        }
    }

    private async Task ProcessRequestAsync(HttpListenerContext context)
    {
        var response = context.Response;
        try
        {
            // 配置 CORS 允许浏览器扩展访问
            response.AddHeader("Access-Control-Allow-Origin", "*");
            response.AddHeader("Access-Control-Allow-Methods", "GET, OPTIONS");
            response.AddHeader("Access-Control-Allow-Headers", "Content-Type");

            if (context.Request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 204;
                response.Close();
                return;
            }

            if (context.Request.Url?.AbsolutePath == "/api/schedule")
            {
                var payload = BuildScheduleData();
                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                });

                byte[] buffer = Encoding.UTF8.GetBytes(json);
                response.ContentType = "application/json; charset=utf-8";
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer);
            }
            else
            {
                response.StatusCode = 404;
            }
        }
        catch (Exception ex)
        {
            response.StatusCode = 500;
            _logger?.LogError(ex, "返回课表数据失败");
        }
        finally
        {
            response.Close();
        }
    }

    private object BuildScheduleData()
    {
        var state = _lessonsService.CurrentState;
        string stateText = state switch
        {
            TimeState.OnClass => "上课中",
            TimeState.Breaking => "课间休息",
            TimeState.PrepareOnClass => "准备上课",
            _ => "放学 / 空闲"
        };

        TimeSpan timeLeft = state switch
        {
            TimeState.OnClass => _lessonsService.OnBreakingTimeLeftTime,
            TimeState.Breaking => _lessonsService.OnClassLeftTime,
            _ => TimeSpan.Zero
        };

        if (timeLeft < TimeSpan.Zero) timeLeft = TimeSpan.Zero;

        return new
        {
            connected = true,
            isLoaded = _lessonsService.IsClassPlanLoaded,
            state = state.ToString(),
            stateText = stateText,
            currentSubject = _lessonsService.CurrentSubject?.Name ?? "无",
            nextSubject = _lessonsService.NextClassSubject?.Name ?? "无",
            timeLeftSeconds = (int)timeLeft.TotalSeconds,
            timeLeftFormatted = $"{(int)timeLeft.TotalMinutes:D2}:{timeLeft.Seconds:D2}",
            timeLayoutName = _lessonsService.CurrentTimeLayoutItem?.TimeType == 0 ? "上课" : "课间",
            updatedAt = DateTimeOffset.Now.ToUnixTimeSeconds()
        };
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        _listener?.Stop();
        _listener?.Close();
        return Task.CompletedTask;
    }
}