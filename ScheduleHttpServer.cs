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
    private readonly IProfileService? _profileService;
    private readonly ILogger<ScheduleHttpServer>? _logger;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private const int Port = 23850;

    public ScheduleHttpServer(ILessonsService lessonsService, IProfileService? profileService = null, ILogger<ScheduleHttpServer>? logger = null)
    {
        _lessonsService = lessonsService;
        _profileService = profileService;
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

        // 计算当前节点进度
        int totalSeconds = 0;
        int elapsedSeconds = 0;
        double progressPercent = 0.0;
        if (_lessonsService.CurrentTimeLayoutItem != null)
        {
            var itemDuration = _lessonsService.CurrentTimeLayoutItem.EndTime - _lessonsService.CurrentTimeLayoutItem.StartTime;
            totalSeconds = (int)Math.Max(0, itemDuration.TotalSeconds);
            if (totalSeconds > 0)
            {
                elapsedSeconds = Math.Clamp(totalSeconds - (int)timeLeft.TotalSeconds, 0, totalSeconds);
                progressPercent = Math.Round((double)elapsedSeconds / totalSeconds * 100.0, 1);
            }
        }

        // 推算当前课程在 Classes 中的索引以及下一节课的索引
        var plan = _lessonsService.CurrentClassPlan;
        var timeLayout = plan?.TimeLayout;
        if (timeLayout == null && plan != null && _profileService?.Profile.TimeLayouts != null)
        {
            _profileService.Profile.TimeLayouts.TryGetValue(plan.TimeLayoutId, out timeLayout);
        }

        int currentLessonIndex = -1;
        int nextLessonIndex = -1;

        if (timeLayout?.Layouts != null && _lessonsService.CurrentSelectedIndex >= 0)
        {
            int lessonCount = 0;
            for (int i = 0; i < timeLayout.Layouts.Count; i++)
            {
                if (timeLayout.Layouts[i].TimeType == 0) // 上课类型时间点
                {
                    if (i == _lessonsService.CurrentSelectedIndex)
                    {
                        currentLessonIndex = lessonCount;
                    }
                    else if (i > _lessonsService.CurrentSelectedIndex && nextLessonIndex == -1)
                    {
                        nextLessonIndex = lessonCount;
                    }
                    lessonCount++;
                }
            }
        }

        // 提取当日完整课表
        var classList = new List<object>();
        if (plan?.Classes != null)
        {
            for (int i = 0; i < plan.Classes.Count; i++)
            {
                var c = plan.Classes[i];
                string subName = "";
                if (_profileService?.Profile.Subjects != null && _profileService.Profile.Subjects.TryGetValue(c.SubjectId, out var sub))
                {
                    subName = sub.Name;
                }

                bool isCurrent = currentLessonIndex >= 0
                    ? i == currentLessonIndex
                    : (!string.IsNullOrEmpty(subName) && subName == _lessonsService.CurrentSubject?.Name);

                classList.Add(new
                {
                    index = i + 1,
                    name = string.IsNullOrEmpty(subName) ? "自习" : subName,
                    isCurrent = isCurrent
                });
            }
        }

        // 计算真正的下一节课科目名称
        string nextSubjectName;
        if (nextLessonIndex >= 0 && plan?.Classes != null && nextLessonIndex < plan.Classes.Count)
        {
            var nextClass = plan.Classes[nextLessonIndex];
            if (_profileService?.Profile.Subjects != null && _profileService.Profile.Subjects.TryGetValue(nextClass.SubjectId, out var nextSub))
            {
                nextSubjectName = string.IsNullOrEmpty(nextSub.Name) ? "自习" : nextSub.Name;
            }
            else
            {
                nextSubjectName = "自习";
            }
        }
        else if (nextLessonIndex == -1 && _lessonsService.CurrentSelectedIndex >= 0)
        {
            // 当前时间点之后今天已无后续课程
            nextSubjectName = "无";
        }
        else
        {
            // 兜底（全天未开始或课表初始状态）
            nextSubjectName = _lessonsService.NextClassSubject?.Name ?? "无";
        }

        return new
        {
            connected = true,
            isLoaded = _lessonsService.IsClassPlanLoaded,
            state = state.ToString(),
            stateText = stateText,
            currentSubject = _lessonsService.CurrentSubject?.Name ?? "无",
            nextSubject = nextSubjectName,
            timeLeftSeconds = (int)timeLeft.TotalSeconds,
            timeLeftFormatted = $"{(int)timeLeft.TotalMinutes:D2}:{timeLeft.Seconds:D2}",
            totalSeconds = totalSeconds,
            elapsedSeconds = elapsedSeconds,
            progressPercent = progressPercent,
            classes = classList,
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