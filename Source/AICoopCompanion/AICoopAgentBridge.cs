using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Verse;

namespace AICoopCompanion
{
    internal static class AICoopAgentBridge
    {
        public const string PipeName = "AICoopCompanion-v1";
        public const int ProtocolVersion = 2;

        private sealed class BridgeRequest
        {
            public int Generation;
            public string Id;
            public string Method;
            public string RawJson;
            public string Response;
            public int State;
            public readonly ManualResetEvent Done = new ManualResetEvent(false);
        }

        private sealed class ResultEvent
        {
            public long Sequence;
            public int Tick;
            public string Type;
            public string Text;
        }

        private static readonly object Gate = new object();
        private static readonly Queue<BridgeRequest> PendingRequests = new Queue<BridgeRequest>();
        private static readonly List<ResultEvent> ResultEvents = new List<ResultEvent>();
        private static readonly List<ResultEvent> PendingStartupEvents = new List<ResultEvent>();
        private static List<string> previousCommandResults = new List<string>();
        private static List<string> previousLogs = new List<string>();
        private static List<string> previousWorkMessages = new List<string>();
        private static NamedPipeServerStream activePipe;
        private static int generation;
        private static bool running;
        private static bool connected;
        private static volatile bool shuttingDown;
        private static DateTime lastAgentHeartbeatUtc;
        private static string sessionId = "-";
        private static string lastError = string.Empty;
        private static long nextEventSequence;

        private static int DecisionIntervalSeconds()
        {
            AICoopSettings settings = AICoopMod.Settings;
            return settings == null ? 30 : System.Math.Max(0, System.Math.Min(120, settings.decisionIntervalSeconds));
        }

        public static string PipePath
        {
            get { return @"\\.\pipe\" + PipeName; }
        }

        public static bool IsRunning
        {
            get { lock (Gate) return running; }
        }

        public static bool IsConnected
        {
            get { lock (Gate) return connected && DateTime.UtcNow - lastAgentHeartbeatUtc < TimeSpan.FromSeconds(90); }
        }

        public static string StatusLabel
        {
            get
            {
                lock (Gate)
                {
                    if (!running) return lastError.NullOrEmpty() ? "未启动" : "启动失败：" + lastError;
                    return IsConnected ? "Agent 已连接" : "等待 Agent 连接";
                }
            }
        }

        public static void NotifyGameLoading()
        {
            Stop();
            lock (Gate) PendingStartupEvents.Clear();
        }

        public static void InstallExitHooks()
        {
            UnityEngine.Application.wantsToQuit += delegate { Shutdown(); return true; };
            UnityEngine.Application.quitting += Shutdown;
        }

        public static void Shutdown()
        {
            shuttingDown = true;
            Stop();
        }

        public static void NotifyGameReady()
        {
            Stop();
        }

        public static void QueueAgentTrigger(string reason, string details)
        {
            ResultEvent item = new ResultEvent
            {
                Tick = Find.TickManager == null ? 0 : Find.TickManager.TicksGame,
                Type = "agent_trigger",
                Text = "reason=" + (reason ?? "unknown") + " details=" + (details ?? string.Empty)
            };
            lock (Gate)
            {
                if (running)
                {
                    item.Sequence = ++nextEventSequence;
                    ResultEvents.Add(item);
                    TrimEvents();
                }
                else
                {
                    PendingStartupEvents.Add(item);
                }
            }
        }

        public static void PumpMainThread()
        {
            AICoopSettings settings = AICoopMod.Settings;
            bool shouldRun = !shuttingDown && settings != null &&
                Current.Game != null && !AICoopAgentRuntime.IsGameLoading;
            if (shouldRun) EnsureStarted();
            else
            {
                Stop();
                return;
            }

            ObserveCommandResults();
            ObserveLogs();
            ObserveMessages();
            int processed = 0;
            while (processed++ < 4)
            {
                BridgeRequest request = null;
                lock (Gate)
                {
                    if (PendingRequests.Count > 0) request = PendingRequests.Dequeue();
                }
                if (request == null) break;
                if (Interlocked.CompareExchange(ref request.State, 1, 0) != 0) continue;

                try
                {
                    request.Response = ProcessRequest(request);
                }
                catch (Exception ex)
                {
                    request.Response = ErrorResponse(request.Id, "internal_error", ex.Message);
                    lock (Gate) lastError = ex.Message;
                }
                finally
                {
                    request.Done.Set();
                }
            }
            ObserveCommandResults();
            ObserveLogs();
            ObserveMessages();
        }

        private static void EnsureStarted()
        {
            int currentGeneration;
            lock (Gate)
            {
                if (running || shuttingDown) return;
                generation++;
                currentGeneration = generation;
                running = true;
                connected = false;
                sessionId = Guid.NewGuid().ToString("N");
                lastError = string.Empty;
                nextEventSequence = 0;
                ResultEvents.Clear();
                AICoopGameComponent component = AICoopGameComponent.Current;
                previousCommandResults = component == null
                    ? new List<string>()
                    : new List<string>(component.CommandResults);
                previousLogs = component == null
                    ? new List<string>()
                    : new List<string>(component.LogLines);
                previousWorkMessages = component == null
                    ? new List<string>()
                    : new List<string>(component.WorkMessages);
                foreach (ResultEvent item in PendingStartupEvents)
                {
                    item.Sequence = ++nextEventSequence;
                    ResultEvents.Add(item);
                }
                PendingStartupEvents.Clear();
                TrimEvents();
            }

            Thread thread = new Thread(new ThreadStart(delegate { ServerLoop(currentGeneration); }));
            thread.IsBackground = true;
            thread.Name = "AICoop Agent Bridge";
            thread.Start();
        }

        private static void Stop()
        {
            NamedPipeServerStream pipe;
            List<BridgeRequest> cancelled = new List<BridgeRequest>();
            lock (Gate)
            {
                if (!running && activePipe == null && PendingRequests.Count == 0) return;
                generation++;
                running = false;
                connected = false;
                sessionId = "-";
                pipe = activePipe;
                activePipe = null;
                while (PendingRequests.Count > 0) cancelled.Add(PendingRequests.Dequeue());
                ResultEvents.Clear();
                previousCommandResults.Clear();
                previousLogs.Clear();
                previousWorkMessages.Clear();
            }

            foreach (BridgeRequest request in cancelled)
            {
                if (Interlocked.CompareExchange(ref request.State, 2, 0) != 0) continue;
                request.Response = ErrorResponse(request.Id, "game_loading", "游戏正在载入或已退出存档。请重新连接。");
                request.Done.Set();
            }
            AICoopPipeLifetime.CloseWithoutWaiting(pipe);
        }

        private static void ServerLoop(int serverGeneration)
        {
            try
            {
                while (IsGenerationActive(serverGeneration))
                {
                    NamedPipeServerStream pipe = null;
                    try
                    {
                        pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1,
                            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                        lock (Gate)
                        {
                            if (!running || generation != serverGeneration)
                            {
                                pipe.Dispose();
                                return;
                            }
                            activePipe = pipe;
                        }

                        IAsyncResult connection = pipe.BeginWaitForConnection(null, null);
                        if (!connection.IsCompleted)
                        {
                            WaitHandle signal = connection.AsyncWaitHandle;
                            while (!signal.WaitOne(100))
                                if (!IsGenerationActive(serverGeneration)) return;
                            pipe.EndWaitForConnection(connection);
                            signal.Close();
                        }
                        else pipe.EndWaitForConnection(connection);
                        lock (Gate)
                        {
                            if (generation != serverGeneration) return;
                            lastError = string.Empty;
                        }

                        using (StreamReader reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true))
                        using (StreamWriter writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true))
                        {
                            writer.AutoFlush = true;
                            while (IsGenerationActive(serverGeneration) && pipe.IsConnected)
                            {
                                string line = reader.ReadLine();
                                if (line == null) break;
                                string response = DispatchToMainThread(serverGeneration, line);
                                writer.WriteLine(response);
                            }
                        }
                    }
                    catch (IOException)
                    {
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                    catch (Exception ex)
                    {
                        lock (Gate)
                        {
                            if (generation == serverGeneration) lastError = ex.Message;
                        }
                        Thread.Sleep(100);
                    }
                    finally
                    {
                        lock (Gate)
                        {
                            if (object.ReferenceEquals(activePipe, pipe)) activePipe = null;
                            // Each CLI request closes its transport; that is not an Agent disconnect.
                        }
                        if (pipe != null)
                        {
                            try { pipe.Dispose(); }
                            catch { }
                        }
                    }
                }
            }
            finally
            {
                lock (Gate)
                {
                    if (generation == serverGeneration)
                    {
                        running = false;
                        connected = false;
                        activePipe = null;
                    }
                }
            }
        }

        private static string DispatchToMainThread(int serverGeneration, string json)
        {
            string id = JsonString(json, "id");
            string method = JsonString(json, "method").ToLowerInvariant();
            if (method.NullOrEmpty()) return ErrorResponse(id, "invalid_request", "缺少 method。可用方法：ping、status、agent_state、execute、results、agent_record。");

            BridgeRequest request = new BridgeRequest
            {
                Generation = serverGeneration,
                Id = id,
                Method = method,
                RawJson = json
            };
            lock (Gate)
            {
                if (!running || generation != serverGeneration)
                    return ErrorResponse(id, "session_expired", "游戏会话已变化，请重新连接。");
                PendingRequests.Enqueue(request);
            }

            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (!request.Done.WaitOne(100))
            {
                if (!IsGenerationActive(serverGeneration))
                {
                    Interlocked.CompareExchange(ref request.State, 2, 0);
                    return ErrorResponse(id, "session_expired", "游戏已退出或正在载入，请求已终止。");
                }
                if (DateTime.UtcNow >= deadline && Interlocked.CompareExchange(ref request.State, 2, 0) == 0)
                    return ErrorResponse(id, "main_thread_timeout", "30 秒内未获得游戏主线程响应，请确认游戏没有卡死或停在载入界面。");
            }
            return request.Response ?? ErrorResponse(id, "empty_response", "游戏没有返回结果。");
        }

        private static string ProcessRequest(BridgeRequest request)
        {
            if (request.Generation != generation || AICoopAgentRuntime.IsGameLoading)
                return ErrorResponse(request.Id, "session_expired", "游戏会话已变化，请重新连接。");

            if (request.Method == "agent_connect")
            {
                if (AICoopGameComponent.Current == null || Find.TickManager == null)
                    return ErrorResponse(request.Id, "no_game", "请先进入游戏存档再连接。");
                lock (Gate) { connected = true; lastAgentHeartbeatUtc = DateTime.UtcNow; }
                AICoopGameComponent.Current?.RequestInitialConfiguration();
                return SuccessResponse(request.Id, "{\"session\":" + Json(CurrentSessionId()) + "}");
            }
            if (request.Method == "agent_disconnect")
            {
                lock (Gate) connected = false;
                AICoopAgentRuntime.CompleteExternalAgentThinking();
                return SuccessResponse(request.Id, "{\"disconnected\":true}");
            }
            if (request.Method == "results" || request.Method == "agent_state" || request.Method == "execute" || request.Method == "agent_record")
            {
                lock (Gate) { if (connected) lastAgentHeartbeatUtc = DateTime.UtcNow; }
            }

            if (request.Method == "ping")
            {
                return SuccessResponse(request.Id,
                    "{\"protocol\":" + ProtocolVersion + ",\"pipe\":" + Json(PipePath) +
                    ",\"session\":" + Json(CurrentSessionId()) + "}");
            }
            if (request.Method == "status")
            {
                if (AICoopGameComponent.Current == null || Find.TickManager == null)
                    return ErrorResponse(request.Id, "no_game", "当前没有可读取的游戏存档。");
                string state = AICoopStateSerializer.BuildPrompt();
                return SuccessResponse(request.Id,
                    "{\"session\":" + Json(CurrentSessionId()) + ",\"tick\":" + Find.TickManager.TicksGame +
                    ",\"decisionIntervalSeconds\":" + DecisionIntervalSeconds() +
                    ",\"state\":" + Json(state) + "}");
            }
            if (request.Method == "agent_state")
            {
                if (AICoopGameComponent.Current == null || Find.TickManager == null)
                    return ErrorResponse(request.Id, "no_game", "当前没有可读取的游戏存档。");
                bool full = JsonBoolean(request.RawJson, "full", false);
                AICoopAgentRuntime.BeginExternalAgentThinking();
                string state;
                try
                {
                    state = AICoopStateSerializer.BuildHarnessState(full);
                }
                catch
                {
                    AICoopAgentRuntime.CompleteExternalAgentThinking();
                    throw;
                }
                AICoopGameComponent.Current.AddLog("[发送给 Agent] " + (full ? "完整殖民地状态" : "殖民地增量状态") + "\n" + state);
                return SuccessResponse(request.Id,
                    "{\"session\":" + Json(CurrentSessionId()) + ",\"tick\":" + Find.TickManager.TicksGame +
                    ",\"decisionIntervalSeconds\":" + DecisionIntervalSeconds() +
                    ",\"mode\":" + Json(full ? "full" : "auto") + ",\"state\":" + Json(state) + "}");
            }
            if (request.Method == "execute")
            {
                string commands = JsonString(request.RawJson, "commands");
                if (commands.NullOrEmpty()) return ErrorResponse(request.Id, "invalid_commands", "execute 请求缺少 commands 字符串。");
                AICoopGameComponent component = AICoopGameComponent.Current;
                if (component == null) return ErrorResponse(request.Id, "no_game", "当前没有可执行命令的游戏存档。");

                if (CompletesThinking(commands)) AICoopAgentRuntime.CompleteExternalAgentThinking();
                component.AddLog("[Agent 原始指令]\n" + commands);
                AICoopCommandDisplay.Log(commands);
                List<string> before = new List<string>(component.CommandResults);
                List<string> logsBefore = new List<string>(component.LogLines);
                AICoopActionExecutor.Execute(commands);
                List<string> after = new List<string>(component.CommandResults);
                List<string> logsAfter = new List<string>(component.LogLines);
                List<string> added = AddedItems(before, after);
                List<string> addedLogs = AddedItems(logsBefore, logsAfter);
                ObserveCommandResults(after);
                ObserveLogs(logsAfter);
                ObserveMessages();
                string record = "命令=" + commands.Replace("\r", " ").Replace("\n", " | ") +
                    "；结果=" + (added.Count == 0 ? "无返回" : string.Join(" | ", added.ToArray()));
                component.RecordAgentExecution(record);
                QueueAgentTrigger("commands_completed", "上一条 CLI 指令已执行；请读取返回的成功、失败和拒绝结果，必要时继续调用 CLI 完成本轮目标；全部动作完成后输出一次队友回复。不要自行读取新的游戏状态。 ");
                return SuccessResponse(request.Id,
                    "{\"accepted\":true,\"results\":" + JsonArray(added) +
                    ",\"logs\":" + JsonArray(addedLogs) + "}");
            }
            if (request.Method == "results")
            {
                long after = JsonLong(request.RawJson, "after", 0);
                List<ResultEvent> events;
                long next;
                lock (Gate)
                {
                    events = ResultEvents.Where(item => item.Sequence > after).ToList();
                    next = nextEventSequence;
                }
                StringBuilder eventJson = new StringBuilder("[");
                for (int i = 0; i < events.Count; i++)
                {
                    if (i > 0) eventJson.Append(',');
                    ResultEvent item = events[i];
                    eventJson.Append("{\"seq\":").Append(item.Sequence)
                        .Append(",\"type\":").Append(Json(item.Type)).Append(",\"tick\":").Append(item.Tick)
                        .Append(",\"text\":").Append(Json(item.Text)).Append('}');
                }
                eventJson.Append(']');
                return SuccessResponse(request.Id,
                    "{\"session\":" + Json(CurrentSessionId()) + ",\"next\":" + next + ",\"events\":" + eventJson +
                    ",\"decisionIntervalSeconds\":" + DecisionIntervalSeconds() + "}");
            }
            if (request.Method == "agent_record")
            {
                AICoopGameComponent component = AICoopGameComponent.Current;
                if (component == null) return ErrorResponse(request.Id, "no_game", "当前没有可记录 Agent 消息的游戏存档。");
                string kind = JsonString(request.RawJson, "kind");
                string text = JsonString(request.RawJson, "text");
                if (text.NullOrEmpty()) return ErrorResponse(request.Id, "invalid_record", "Agent 记录内容为空。");
                string label = kind == "player_reply" ? "Agent 对玩家回复" : kind == "tool" ? "Agent 操作过程" :
                    (kind == "input" ? "发送给 Agent" : "Agent 思考与输出");
                component.AddLog("[" + label + "]\n" + text);
                if (kind == "output" || kind == "player_reply")
                {
                    component.AddChatMessage("AI", text);
                }
                if (kind == "round_end") AICoopAgentRuntime.CompleteExternalAgentThinking();
                if (kind != "input") component.RecordAgentExecution(label + "=" + text);
                return SuccessResponse(request.Id, "{\"recorded\":true}");
            }
            return ErrorResponse(request.Id, "unknown_method", "未知 method。可用方法：ping、status、agent_state、execute、results、agent_record。");
        }

        private static void ObserveCommandResults()
        {
            AICoopGameComponent component = AICoopGameComponent.Current;
            if (component == null) return;
            ObserveCommandResults(new List<string>(component.CommandResults));
        }

        private static void ObserveCommandResults(List<string> current)
        {
            lock (Gate)
            {
                List<string> added = AddedItems(previousCommandResults, current);
                previousCommandResults = new List<string>(current);
                AddEvents("command_result", added);
            }
        }

        private static void ObserveLogs()
        {
            AICoopGameComponent component = AICoopGameComponent.Current;
            if (component == null) return;
            ObserveLogs(new List<string>(component.LogLines));
        }

        private static void ObserveLogs(List<string> current)
        {
            lock (Gate)
            {
                List<string> added = AddedItems(previousLogs, current);
                previousLogs = new List<string>(current);
                AddEvents("log", added);
            }
        }

        private static void ObserveMessages()
        {
            AICoopGameComponent component = AICoopGameComponent.Current;
            if (component == null) return;
            List<string> currentWork = new List<string>(component.WorkMessages);
            lock (Gate)
            {
                List<string> workAdded = AddedItems(previousWorkMessages, currentWork);
                previousWorkMessages = currentWork;
                AddEvents("work_message", workAdded);
            }
        }

        private static void AddEvents(string type, IEnumerable<string> values)
        {
            foreach (string text in values)
            {
                ResultEvents.Add(new ResultEvent
                {
                    Sequence = ++nextEventSequence,
                    Tick = Find.TickManager == null ? 0 : Find.TickManager.TicksGame,
                    Type = type,
                    Text = text
                });
            }
            TrimEvents();
        }

        private static void TrimEvents()
        {
            while (ResultEvents.Count > 200) ResultEvents.RemoveAt(0);
        }

        private static bool CompletesThinking(string commands)
        {
            return !(commands ?? string.Empty).Trim().NullOrEmpty();
        }

        private static List<string> AddedItems(IList<string> previous, IList<string> current)
        {
            int maxOverlap = Math.Min(previous == null ? 0 : previous.Count, current == null ? 0 : current.Count);
            int overlap = 0;
            for (int candidate = maxOverlap; candidate >= 0; candidate--)
            {
                bool matches = true;
                for (int i = 0; i < candidate; i++)
                {
                    if (!string.Equals(previous[previous.Count - candidate + i], current[i], StringComparison.Ordinal))
                    {
                        matches = false;
                        break;
                    }
                }
                if (!matches) continue;
                overlap = candidate;
                break;
            }
            List<string> added = new List<string>();
            if (current != null)
            {
                for (int i = overlap; i < current.Count; i++) added.Add(current[i]);
            }
            return added;
        }

        private static bool IsGenerationActive(int value)
        {
            lock (Gate) return running && generation == value;
        }

        private static string CurrentSessionId()
        {
            lock (Gate) return sessionId;
        }

        private static string JsonString(string json, string key)
        {
            System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(json ?? string.Empty, "\\\"" + key + "\\\"\\s*:\\s*\\\"((?:\\\\.|[^\\\"])*)\\\"");
            if (!match.Success) return string.Empty;
            try
            {
                return System.Text.RegularExpressions.Regex.Unescape(match.Groups[1].Value);
            }
            catch
            {
                return match.Groups[1].Value;
            }
        }

        private static long JsonLong(string json, string key, long fallback)
        {
            Match match = Regex.Match(json ?? string.Empty, "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*(-?\\d+)");
            long value;
            return match.Success && Int64.TryParse(match.Groups[1].Value, out value) ? value : fallback;
        }

        private static bool JsonBoolean(string json, string key, bool fallback)
        {
            Match match = Regex.Match(json ?? string.Empty, "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*(true|false)", RegexOptions.IgnoreCase);
            bool value;
            return match.Success && Boolean.TryParse(match.Groups[1].Value, out value) ? value : fallback;
        }

        private static string SuccessResponse(string id, string resultJson)
        {
            return "{\"id\":" + Json(id) + ",\"ok\":true,\"result\":" + resultJson + "}";
        }

        private static string ErrorResponse(string id, string code, string message)
        {
            return "{\"id\":" + Json(id) + ",\"ok\":false,\"error\":{\"code\":" + Json(code) +
                ",\"message\":" + Json(message) + "}}";
        }

        private static string JsonArray(IEnumerable<string> values)
        {
            return "[" + string.Join(",", (values ?? Enumerable.Empty<string>()).Select(Json).ToArray()) + "]";
        }

        private static string Json(string value)
        {
            StringBuilder result = new StringBuilder((value == null ? 0 : value.Length) + 2);
            result.Append('"');
            foreach (char ch in value ?? string.Empty)
            {
                switch (ch)
                {
                    case '"': result.Append("\\\""); break;
                    case '\\': result.Append("\\\\"); break;
                    case '\b': result.Append("\\b"); break;
                    case '\f': result.Append("\\f"); break;
                    case '\n': result.Append("\\n"); break;
                    case '\r': result.Append("\\r"); break;
                    case '\t': result.Append("\\t"); break;
                    default:
                        if (ch < 32) result.Append("\\u").Append(((int)ch).ToString("x4"));
                        else result.Append(ch);
                        break;
                }
            }
            result.Append('"');
            return result.ToString();
        }
    }
}
