using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using QuanLyKhachSan.Models;
using QuanLyKhachSan.Models.AI;
using QuanLyKhachSan.Models.Enums;

namespace QuanLyKhachSan.Services.AI
{
    public class AiChatService
    {
        private readonly HttpClient _httpClient;
        private readonly AiPromptBuilder _promptBuilder;
        private readonly AiToolExecutor _toolExecutor;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AiChatService> _logger;
        private static readonly ConcurrentDictionary<string, AiChatSession> _sessions = new();
        private static readonly string SessionCacheDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Storage", "ai_sessions");

        public AiChatService(
            IHttpClientFactory httpClientFactory,
            AiPromptBuilder promptBuilder,
            AiToolExecutor toolExecutor,
            IConfiguration configuration,
            ILogger<AiChatService> logger)
        {
            _httpClient = httpClientFactory.CreateClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            _promptBuilder = promptBuilder;
            _toolExecutor = toolExecutor;
            _configuration = configuration;
            _logger = logger;
        }

        public AiChatSession CreateSession(UserRole role, int? userId)
        {
            string tier = AiToolRegistry.GetTierName(role);
            var session = new AiChatSession { UserId = userId, UserTier = tier };
            _sessions[session.SessionId] = session;
            SaveSessionToDisk(session);
            return session;
        }

        private AiChatSession GetOrCreateSession(AiChatRequest request, UserRole role, int? userId)
        {
            string? reqId = string.IsNullOrWhiteSpace(request.SessionId) ? null : request.SessionId.Trim();
            AiChatSession? session = null;

            // 1. Kiểm tra RAM cache
            if (!string.IsNullOrEmpty(reqId) && _sessions.TryGetValue(reqId, out session))
            {
                session.UserTier = AiToolRegistry.GetTierName(role);
                if (userId.HasValue) session.UserId = userId;
            }

            // 2. Kiểm tra Disk Cache (nếu server vừa reload / restart)
            if (session == null && !string.IsNullOrEmpty(reqId))
            {
                session = LoadSessionFromDisk(reqId);
                if (session != null)
                {
                    session.UserTier = AiToolRegistry.GetTierName(role);
                    if (userId.HasValue) session.UserId = userId;
                    _sessions[session.SessionId] = session;
                }
            }

            // 3. Nếu chưa có, tạo phiên mới (giữ lại SessionId của client nếu client đã gửi)
            if (session == null)
            {
                session = new AiChatSession
                {
                    SessionId = !string.IsNullOrEmpty(reqId) ? reqId : Guid.NewGuid().ToString(),
                    UserTier = AiToolRegistry.GetTierName(role),
                    UserId = userId,
                    CreatedAt = DateTime.UtcNow,
                    LastActivityAt = DateTime.UtcNow
                };
                _sessions[session.SessionId] = session;
            }

            // 4. Đồng bộ / Khôi phục lịch sử chat từ client (nếu client có gửi history cache và session đang thiếu)
            if (request.History != null && request.History.Count > 0)
            {
                foreach (var hMsg in request.History)
                {
                    if (string.IsNullOrWhiteSpace(hMsg.Content)) continue;
                    bool exists = session.Messages.Any(m =>
                        string.Equals(m.Role, hMsg.Role, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(m.Content.Trim(), hMsg.Content.Trim(), StringComparison.Ordinal));

                    if (!exists)
                    {
                        session.Messages.Add(new AiChatMessage
                        {
                            Role = hMsg.Role.ToLowerInvariant(),
                            Content = hMsg.Content.Trim(),
                            Timestamp = hMsg.Timestamp != default ? hMsg.Timestamp : DateTime.UtcNow
                        });
                    }
                }
            }

            return session;
        }

        private void SaveSessionToDisk(AiChatSession session)
        {
            try
            {
                if (!Directory.Exists(SessionCacheDirectory))
                {
                    Directory.CreateDirectory(SessionCacheDirectory);
                }

                var cleanId = Regex.Replace(session.SessionId, @"[^a-zA-Z0-9_\-]", "");
                if (string.IsNullOrEmpty(cleanId)) return;

                var filePath = Path.Combine(SessionCacheDirectory, $"{cleanId}.json");
                var json = JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = false });
                File.WriteAllText(filePath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                _logger.LogTrace(ex, "Lỗi khi lưu cache phiên chat {SessionId}", session.SessionId);
            }
        }

        private AiChatSession? LoadSessionFromDisk(string sessionId)
        {
            try
            {
                var cleanId = Regex.Replace(sessionId, @"[^a-zA-Z0-9_\-]", "");
                if (string.IsNullOrEmpty(cleanId)) return null;

                var filePath = Path.Combine(SessionCacheDirectory, $"{cleanId}.json");
                if (File.Exists(filePath))
                {
                    var json = File.ReadAllText(filePath, Encoding.UTF8);
                    return JsonSerializer.Deserialize<AiChatSession>(json);
                }
            }
            catch (Exception ex)
            {
                _logger.LogTrace(ex, "Lỗi khi đọc cache phiên chat {SessionId}", sessionId);
            }
            return null;
        }

        private void DeleteSessionFromDisk(string sessionId)
        {
            try
            {
                var cleanId = Regex.Replace(sessionId, @"[^a-zA-Z0-9_\-]", "");
                if (string.IsNullOrEmpty(cleanId)) return;

                var filePath = Path.Combine(SessionCacheDirectory, $"{cleanId}.json");
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogTrace(ex, "Lỗi khi xóa cache phiên chat {SessionId}", sessionId);
            }
        }

        public async Task<AiChatResponse> ProcessChatAsync(AiChatRequest request, UserRole role, int? userId = null)
        {
            // 1. Phục hồi hoặc tạo phiên chat từ cache (Memory -> Disk Cache -> Client History Rehydration)
            var session = GetOrCreateSession(request, role, userId);
            var sessionId = session.SessionId;

            session.LastActivityAt = DateTime.UtcNow;
            string userMsg = (request.Message ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(userMsg))
            {
                bool isDuplicate = session.Messages.Count > 0 &&
                    session.Messages.Last().Role.Equals("user", StringComparison.OrdinalIgnoreCase) &&
                    session.Messages.Last().Content.Trim() == userMsg;

                if (!isDuplicate)
                {
                    session.Messages.Add(new AiChatMessage { Role = "user", Content = userMsg, Timestamp = DateTime.UtcNow });
                }
            }

            List<AiQuickAction> quickActions = new();
            AiFunctionCallResult? functionResult = null;
            string? reply = null;

            // 2. Luôn truy vấn dữ liệu phòng trống thực tế mới nhất từ CSDL
            functionResult = await _toolExecutor.ExecuteToolAsync("check_room_availability", null, role, session.UserId);

            // 2b. NẾU LÀ ADMIN HOẶC MANAGER: Truy vấn thêm báo cáo doanh thu & chỉ số vận hành kinh doanh thực tế
            AiFunctionCallResult? metricsResult = null;
            if (role is UserRole.Admin or UserRole.Manager)
            {
                metricsResult = await _toolExecutor.ExecuteToolAsync("get_occupancy_and_revenue_metrics", null, role, session.UserId);
            }

            // 3. TỔNG HỢP TOÀN BỘ NGỮ CẢNH & THÔNG TIN TỪ TOÀN BỘ LỊCH SỬ CHAT CỦA PHIÊN (MULTI-TURN CHAT SYNTHESIS)
            SynthesizeContextFromHistory(session, functionResult);

            // 4. Tra cứu booking nếu người dùng nhắc đến mã đặt phòng
            AiFunctionCallResult? bookingLookupResult = null;
            var bookingMatch = Regex.Match(userMsg, @"(BK\d{6,12}|BK-[A-Za-z0-9]+|[A-Za-z0-9]{8,12})", RegexOptions.IgnoreCase);
            if (bookingMatch.Success && !userMsg.ToLowerInvariant().Contains("đặt phòng"))
            {
                bookingLookupResult = await _toolExecutor.ExecuteToolAsync("lookup_booking", new { bookingCode = bookingMatch.Value }, role, session.UserId);
            }

            // 5. Xây dựng System Prompt kết hợp dữ liệu Database thực tế, Doanh thu và Bộ nhớ thông tin đã tổng hợp
            string systemPrompt = BuildEnrichedSystemPrompt(role, functionResult, bookingLookupResult, session.BookingDraft, metricsResult);

            // 5. Lấy cấu hình AI từ .env / Configuration (Ưu tiên file .env, mặc định gemini-3.8-flash)
            var apiKey = GetConfigValue("AI_API_KEY");
            var baseUrl = GetConfigValue("AI_API_BASE_URL", "https://generativelanguage.googleapis.com");
            var configuredModel = GetConfigValue("AI_MODEL_NAME", "gemini-3.8-flash");

            double temperature = 0.2;
            if (double.TryParse(GetConfigValue("AI_TEMPERATURE", "0.2"), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsedTemp))
            {
                temperature = parsedTemp;
            }

            int maxTokens = 1500;
            if (int.TryParse(GetConfigValue("AI_MAX_TOKENS", "1500"), out var parsedTokens))
            {
                maxTokens = parsedTokens;
            }

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                // Gọi API Gemini với model từ .env (hoặc mặc định gemini-3.8-flash)
                reply = await TryCallGeminiAsync(systemPrompt, session.Messages, userMsg, apiKey, baseUrl, configuredModel, temperature, maxTokens);

                // Nếu Gemini kích hoạt tạo đơn qua thẻ hành động [ACTION:create_booking {...}]
                if (!string.IsNullOrWhiteSpace(reply))
                {
                    var actionMatch = Regex.Match(reply, @"\[ACTION:create_booking\s*(\{.*?\})\]", RegexOptions.Singleline);
                    if (actionMatch.Success)
                    {
                        try
                        {
                            var jsonParams = actionMatch.Groups[1].Value;
                            using var doc = JsonDocument.Parse(jsonParams);
                            var elem = doc.RootElement;

                            var createBookingResult = await _toolExecutor.ExecuteToolAsync("create_booking", elem, role, session.UserId);
                            if (createBookingResult.Success && createBookingResult.Data != null)
                            {
                                dynamic b = createBookingResult.Data;
                                session.BookingDraft.LastCreatedBookingCode = b.BookingCode;
                                functionResult = createBookingResult;

                                string confirmationCard = $"\n\n🎉 **XÁC NHẬN ĐẶT PHÒNG THÀNH CÔNG!**\n" +
                                                          $"• **Mã đặt phòng:** `{b.BookingCode}`\n" +
                                                          $"• **Hạng phòng:** {b.RoomTypeName}\n" +
                                                          $"• **Khách hàng:** {b.GuestName} - {b.Phone}\n" +
                                                          $"• **Thời gian lưu trú:** {b.CheckInDate} đến {b.CheckOutDate} ({b.Nights} đêm)\n" +
                                                          $"• **Số khách:** {b.NumGuests} khách\n" +
                                                          $"• **Tổng thanh toán:** {((decimal)b.TotalAmount):N0}₫\n" +
                                                          $"• **Trạng thái:** Chờ thanh toán / Xác nhận tại quầy\n\n" +
                                                          $"👉 Quý khách có thể bấm **Thanh toán ngay** bên dưới hoặc thanh toán tại quầy lễ tân khi nhận phòng ạ!";

                                reply = reply.Replace(actionMatch.Value, confirmationCard);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Lỗi khi thực thi [ACTION:create_booking] từ phản hồi của Gemini");
                        }
                    }
                }
            }
            else
            {
                _logger.LogWarning("AI_API_KEY chưa được cấu hình trong .env hoặc appsettings.json.");
            }

            // 6. Nếu không gọi được Gemini (chưa có key, lỗi mạng, lỗi model), sử dụng Tiếp tân ảo dự phòng thông minh
            if (string.IsNullOrWhiteSpace(reply))
            {
                reply = await GenerateConversationalFallbackResponseAsync(userMsg, session, functionResult, bookingLookupResult, bookingMatch.Value, role, metricsResult);
            }

            // 7. Bổ sung các Quick Actions tiện ích theo vai trò (Admin/Manager hoặc Guest)
            GenerateQuickActions(userMsg, session, functionResult, quickActions, role);

            session.Messages.Add(new AiChatMessage { Role = "assistant", Content = reply, Timestamp = DateTime.UtcNow });
            SaveSessionToDisk(session);

            return new AiChatResponse
            {
                SessionId = sessionId,
                Reply = reply,
                QuickActions = quickActions,
                FunctionResult = functionResult
            };
        }

        // Danh sách các model Gemini 3 chính thức từ Google AI Studio (ai.google.dev/gemini-api/docs/models#gemini-3-stable)
        private static readonly string[] Gemini3CandidateModels = new[]
        {
            "gemini-3.8-flash",
            "gemini-3.5-flash-lite",
            "gemini-3.7-flash",
            "gemini-3.5-flash",
            "gemini-3.6-flash",
            "gemini-3.1-flash-lite",
            "gemini-3-flash-preview",
            "gemini-3.1-pro-preview"
        };

        private async Task<string?> TryCallGeminiAsync(
            string systemPrompt, 
            List<AiChatMessage> history, 
            string userMsg, 
            string apiKey, 
            string baseUrl, 
            string configuredModel,
            double temperature,
            int maxTokens)
        {
            var modelsToTry = new List<string>();
            var primaryModel = !string.IsNullOrWhiteSpace(configuredModel) 
                ? configuredModel.Trim() 
                : "gemini-3.8-flash";
            
            modelsToTry.Add(primaryModel);

            // Bổ sung các model dự phòng từ danh sách Gemini 3 chính thức
            foreach (var candidate in Gemini3CandidateModels)
            {
                if (!modelsToTry.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                {
                    modelsToTry.Add(candidate);
                }
            }

            foreach (var model in modelsToTry)
            {
                try
                {
                    var result = await CallGeminiModelAsync(systemPrompt, history, userMsg, apiKey, baseUrl, model, temperature, maxTokens);
                    if (!string.IsNullOrWhiteSpace(result))
                    {
                        if (!string.Equals(model, primaryModel, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogInformation("Gemini model chính '{PrimaryModel}' không phản hồi, đã chuyển sang model dự phòng thành công: '{BackupModel}'", primaryModel, model);
                        }
                        return result;
                    }
                    _logger.LogWarning("Gemini model '{Model}' không phản hồi hợp lệ, đang thử model dự phòng tiếp theo...", model);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Gọi Gemini model '{Model}' phát sinh lỗi, đang chuyển sang model dự phòng...", model);
                }
            }

            _logger.LogError("Tất cả các model Gemini 3 (chính & dự phòng) đều không phản hồi.");
            return null;
        }

        private async Task<string?> CallGeminiModelAsync(
            string systemPrompt,
            List<AiChatMessage> history,
            string userMsg,
            string apiKey,
            string baseUrl,
            string modelName,
            double temperature,
            int maxTokens)
        {
            var escapedKey = Uri.EscapeDataString(apiKey.Trim());
            var trimmedBase = (baseUrl ?? "https://generativelanguage.googleapis.com").Trim().TrimEnd('/');

            string url;
            if (trimmedBase.EndsWith(":generateContent", StringComparison.OrdinalIgnoreCase))
            {
                url = $"{trimmedBase}?key={escapedKey}";
            }
            else if (trimmedBase.Contains("/models/"))
            {
                url = $"{trimmedBase}:generateContent?key={escapedKey}";
            }
            else if (trimmedBase.EndsWith("/v1beta", StringComparison.OrdinalIgnoreCase) || trimmedBase.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                url = $"{trimmedBase}/models/{modelName}:generateContent?key={escapedKey}";
            }
            else
            {
                url = $"{trimmedBase}/v1beta/models/{modelName}:generateContent?key={escapedKey}";
            }

            var contents = new List<object>();

            // Lấy tối đa 16 tin nhắn lịch sử trước đó (không bao gồm tin nhắn hiện tại)
            var priorMessages = history
                .Where(m => !string.IsNullOrWhiteSpace(m.Content))
                .ToList();

            if (priorMessages.Count > 0 &&
                priorMessages.Last().Role.Equals("user", StringComparison.OrdinalIgnoreCase) &&
                priorMessages.Last().Content.Trim() == userMsg.Trim())
            {
                priorMessages.RemoveAt(priorMessages.Count - 1);
            }

            if (priorMessages.Count > 16)
            {
                priorMessages = priorMessages.TakeLast(16).ToList();
            }

            string lastRole = string.Empty;
            foreach (var msg in priorMessages)
            {
                string role = msg.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) || 
                              msg.Role.Equals("model", StringComparison.OrdinalIgnoreCase)
                    ? "model" 
                    : "user";

                if (contents.Count == 0 && role != "user") continue;
                if (role == lastRole) continue;

                contents.Add(new
                {
                    role = role,
                    parts = new object[] { new { text = msg.Content } }
                });
                lastRole = role;
            }

            if (lastRole == "user" && contents.Count > 0)
            {
                contents.RemoveAt(contents.Count - 1);
            }

            contents.Add(new
            {
                role = "user",
                parts = new object[] { new { text = userMsg } }
            });

            var payload = new
            {
                system_instruction = new
                {
                    parts = new object[] { new { text = systemPrompt } }
                },
                contents = contents,
                generationConfig = new
                {
                    temperature = temperature,
                    maxOutputTokens = maxTokens
                }
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json"
            );

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("x-goog-api-key", apiKey.Trim());
            request.Content = jsonContent;

            var response = await _httpClient.SendAsync(request);
            var responseString = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Gemini API call to model '{Model}' failed with status {StatusCode} ({StatusText}): {ResponseBody}", 
                    modelName, (int)response.StatusCode, response.StatusCode, responseString);
                return null;
            }

            using var doc = JsonDocument.Parse(responseString);
            if (doc.RootElement.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
            {
                var first = candidates[0];
                if (first.TryGetProperty("content", out var content) &&
                    content.TryGetProperty("parts", out var parts) &&
                    parts.GetArrayLength() > 0)
                {
                    var text = parts[0].GetProperty("text").GetString();
                    return text?.Trim();
                }
            }

            return null;
        }

        private void SynthesizeContextFromHistory(AiChatSession session, AiFunctionCallResult? roomResult)
        {
            var draft = session.BookingDraft;
            DateOnly today = DateOnly.FromDateTime(DateTime.Today);

            // Duyệt tuần tự qua toàn bộ lịch sử trò chuyện để tổng hợp thông tin mà khách đã từng cung cấp
            for (int i = 0; i < session.Messages.Count; i++)
            {
                var msg = session.Messages[i];
                if (!msg.Role.Equals("user", StringComparison.OrdinalIgnoreCase)) continue;

                string text = msg.Content.Trim();
                string lower = text.ToLowerInvariant();

                // 1. Số điện thoại (Bắt số 10-11 chữ số di động VN)
                var phoneMatch = Regex.Match(text, @"(?:\+84|0)(3[2-9]|5[6|8|9]|7[0|6-9]|8[1-5|8|9]|9[0-4|6-9])\d{7}|0\d{9}");
                if (phoneMatch.Success)
                {
                    draft.Phone = phoneMatch.Value;
                    session.SynthesizedFacts["phone"] = draft.Phone;
                }

                // 2. Email
                var emailMatch = Regex.Match(text, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}");
                if (emailMatch.Success)
                {
                    draft.Email = emailMatch.Value;
                    session.SynthesizedFacts["email"] = draft.Email;
                }

                // 3. Số lượng khách
                var guestMatch = Regex.Match(text, @"(\d+)\s*(?:người|khách|pax|chỗ|vé)", RegexOptions.IgnoreCase);
                if (guestMatch.Success && int.TryParse(guestMatch.Groups[1].Value, out var g) && g > 0 && g <= 10)
                {
                    draft.NumGuests = g;
                    session.SynthesizedFacts["guests"] = g.ToString();
                }
                else if (lower.Contains("hai người") || lower.Contains("2 người"))
                {
                    draft.NumGuests = 2;
                }
                else if (lower.Contains("một người") || lower.Contains("1 người") || lower.Contains("phòng đơn"))
                {
                    draft.NumGuests = 1;
                }

                // 4. Hạng phòng theo CSDL & Từ đồng nghĩa
                if (roomResult != null && roomResult.Success && roomResult.Data != null)
                {
                    dynamic data = roomResult.Data;
                    var roomTypes = (IEnumerable<dynamic>)data.RoomTypes;
                    foreach (var rt in roomTypes)
                    {
                        string name = ((string)rt.RoomTypeName).ToLowerInvariant();
                        if (lower.Contains(name) ||
                            (name.Contains("deluxe") && lower.Contains("deluxe")) ||
                            (name.Contains("suite") && (lower.Contains("suite") || lower.Contains("vip") || lower.Contains("hạng sang") || lower.Contains("cao cấp"))) ||
                            (name.Contains("standard") && (lower.Contains("standard") || lower.Contains("tiêu chuẩn") || lower.Contains("phòng đơn"))) ||
                            (name.Contains("family") && (lower.Contains("family") || lower.Contains("gia đình"))) ||
                            (name.Contains("executive") && lower.Contains("executive")))
                        {
                            draft.RoomTypeId = rt.RoomTypeId;
                            draft.RoomTypeName = rt.RoomTypeName;
                            session.SynthesizedFacts["room_type"] = draft.RoomTypeName;
                            break;
                        }
                    }
                }

                // 5. Ngày nhận phòng & Ngày trả phòng
                if (lower.Contains("hôm nay") || lower.Contains("tối nay"))
                {
                    draft.CheckInDate = today;
                    if (!draft.CheckOutDate.HasValue || draft.CheckOutDate <= draft.CheckInDate)
                        draft.CheckOutDate = today.AddDays(1);
                }
                else if (lower.Contains("ngày mai") || lower.Contains("mai"))
                {
                    draft.CheckInDate = today.AddDays(1);
                    if (!draft.CheckOutDate.HasValue || draft.CheckOutDate <= draft.CheckInDate)
                        draft.CheckOutDate = today.AddDays(2);
                }
                else if (lower.Contains("ngày kia"))
                {
                    draft.CheckInDate = today.AddDays(2);
                    if (!draft.CheckOutDate.HasValue || draft.CheckOutDate <= draft.CheckInDate)
                        draft.CheckOutDate = today.AddDays(3);
                }

                // Số đêm: ví dụ "2 đêm", "3 đêm"
                var nightsMatch = Regex.Match(text, @"(\d+)\s*đêm", RegexOptions.IgnoreCase);
                if (nightsMatch.Success && int.TryParse(nightsMatch.Groups[1].Value, out var n) && n > 0)
                {
                    var cin = draft.CheckInDate ?? today;
                    draft.CheckInDate = cin;
                    draft.CheckOutDate = cin.AddDays(n);
                }

                // Dạng khoảng ngày cụ thể: 27/09 đến 29/09 hoặc 27/9 - 29/9
                var dateRangeMatch = Regex.Match(text, @"(?:từ\s*)?(\d{1,2})[/-](\d{1,2})(?:[/-](\d{2,4}))?\s*(?:đến|-|tới)\s*(\d{1,2})[/-](\d{1,2})(?:[/-](\d{2,4}))?", RegexOptions.IgnoreCase);
                if (dateRangeMatch.Success)
                {
                    try
                    {
                        int inD = int.Parse(dateRangeMatch.Groups[1].Value);
                        int inM = int.Parse(dateRangeMatch.Groups[2].Value);
                        int inY = dateRangeMatch.Groups[3].Success ? (dateRangeMatch.Groups[3].Value.Length == 2 ? 2000 + int.Parse(dateRangeMatch.Groups[3].Value) : int.Parse(dateRangeMatch.Groups[3].Value)) : today.Year;

                        int outD = int.Parse(dateRangeMatch.Groups[4].Value);
                        int outM = int.Parse(dateRangeMatch.Groups[5].Value);
                        int outY = dateRangeMatch.Groups[6].Success ? (dateRangeMatch.Groups[6].Value.Length == 2 ? 2000 + int.Parse(dateRangeMatch.Groups[6].Value) : int.Parse(dateRangeMatch.Groups[6].Value)) : inY;

                        draft.CheckInDate = new DateOnly(inY, inM, inD);
                        draft.CheckOutDate = new DateOnly(outY, outM, outD);
                    }
                    catch {}
                }

                // 6. Họ và tên người nhận phòng
                var nameMatch = Regex.Match(text, @"(?:tên là|tôi là|mình là|tên tôi là|tên em là|khách là)\s+([A-ZÀ-Ỹa-zà-ỹ\s]{2,30})", RegexOptions.IgnoreCase);
                if (nameMatch.Success)
                {
                    draft.GuestName = nameMatch.Groups[1].Value.Trim();
                    session.SynthesizedFacts["guest_name"] = draft.GuestName;
                }
                else
                {
                    var prefixNameMatch = Regex.Match(text, @"(?:anh|chị|chú|bác)\s+([A-ZÀ-Ỹa-zà-ỹ]{2,20})", RegexOptions.IgnoreCase);
                    if (prefixNameMatch.Success && string.IsNullOrWhiteSpace(draft.GuestName))
                    {
                        string candidateName = prefixNameMatch.Groups[1].Value.Trim();
                        string lowerCand = candidateName.ToLowerInvariant();
                        if (lowerCand != "ơi" && lowerCand != "cần" && lowerCand != "muốn" && lowerCand != "đặt" && lowerCand != "hỏi" && lowerCand != "xem" && lowerCand != "cho")
                        {
                            draft.GuestName = candidateName;
                            session.SynthesizedFacts["guest_name"] = draft.GuestName;
                        }
                    }
                    else if (i > 0 && string.IsNullOrWhiteSpace(draft.GuestName))
                    {
                        var prevAssistantMsg = session.Messages[i - 1];
                        if (prevAssistantMsg.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
                        {
                            string prevLower = prevAssistantMsg.Content.ToLowerInvariant();
                            if (prevLower.Contains("tên") || prevLower.Contains("họ và tên") || prevLower.Contains("người nhận phòng"))
                            {
                                var words = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                if (words.Length >= 1 && words.Length <= 4 && 
                                    !lower.Contains("phòng") && !lower.Contains("giá") && !lower.Contains("ngày") && 
                                    !lower.Contains("bao nhiêu") && !lower.Contains("đêm") && !lower.Contains("ok") && !lower.Contains("alo"))
                                {
                                    draft.GuestName = text.Trim();
                                    session.SynthesizedFacts["guest_name"] = draft.GuestName;
                                }
                            }
                        }
                    }
                }

                // 7. Yêu cầu / Ghi chú đặc biệt
                if (lower.Contains("tầng cao") || lower.Contains("view biển") || lower.Contains("view đẹp") || 
                    lower.Contains("không hút thuốc") || lower.Contains("yên tĩnh") || lower.Contains("check-in muộn") || 
                    lower.Contains("checkin muộn") || lower.Contains("ăn sáng"))
                {
                    var notesList = new List<string>();
                    if (lower.Contains("tầng cao")) notesList.Add("Tầng cao");
                    if (lower.Contains("view biển") || lower.Contains("view đẹp")) notesList.Add("View đẹp/biển");
                    if (lower.Contains("không hút thuốc")) notesList.Add("Phòng không hút thuốc");
                    if (lower.Contains("yên tĩnh")) notesList.Add("Phòng yên tĩnh");
                    if (lower.Contains("check-in muộn") || lower.Contains("checkin muộn")) notesList.Add("Check-in muộn");
                    if (notesList.Count > 0)
                    {
                        draft.Notes = string.Join(", ", notesList);
                        session.SynthesizedFacts["notes"] = draft.Notes;
                    }
                }

                // 8. Hình thức thanh toán (Tiền mặt tại quầy / Chuyển khoản VietQR / MoMo / Thẻ)
                if (lower.Contains("tiền mặt") || lower.Contains("tại quầy") || lower.Contains("khi nhận phòng") || lower.Contains("nhận phòng thanh toán"))
                {
                    draft.PaymentMethod = "Tiền mặt tại quầy";
                    session.SynthesizedFacts["payment_method"] = draft.PaymentMethod;
                }
                else if (lower.Contains("chuyển khoản") || lower.Contains("vietqr") || lower.Contains("ngân hàng") || lower.Contains("bằng qr") || lower.Contains("quét qr"))
                {
                    draft.PaymentMethod = "Chuyển khoản VietQR";
                    session.SynthesizedFacts["payment_method"] = draft.PaymentMethod;
                }
                else if (lower.Contains("momo") || lower.Contains("ví điện tử"))
                {
                    draft.PaymentMethod = "Ví MoMo";
                    session.SynthesizedFacts["payment_method"] = draft.PaymentMethod;
                }
                else if (lower.Contains("thẻ") || lower.Contains("visa") || lower.Contains("mastercard") || lower.Contains("quẹt thẻ"))
                {
                    draft.PaymentMethod = "Thẻ ngân hàng";
                    session.SynthesizedFacts["payment_method"] = draft.PaymentMethod;
                }
            }
        }

        private string BuildEnrichedSystemPrompt(
            UserRole role, 
            AiFunctionCallResult? roomResult, 
            AiFunctionCallResult? bookingResult,
            AiBookingDraft draft,
            AiFunctionCallResult? metricsResult = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine(_promptBuilder.BuildPromptForRole(role));
            sb.AppendLine();

            if (role is UserRole.Admin or UserRole.Manager)
            {
                sb.AppendLine("=== VAI TRÒ ĐẶC BIỆT: TRỢ LÝ ĐIỀU HÀNH & BÁO CÁO KINH DOANH CHO BAN QUẢN LÝ / ADMIN ===");
                sb.AppendLine("Bạn đang đối thoại trực tiếp với QUẢN TRỊ VIÊN / QUẢN LÝ (Admin / Manager) của khách sạn Sun Hotel.");
                sb.AppendLine("Quy tắc ứng xử và giải đáp câu hỏi của Quản trị viên:");
                sb.AppendLine("1. Bạn có TOÀN QUYỀN TRUY CẬP và BẮT BUỘC cung cấp số liệu doanh thu, công suất phòng, chỉ số tài chính, tình trạng buồng phòng và các chỉ số kinh doanh khi Admin hỏi.");
                sb.AppendLine("2. Tuyệt đối KHÔNG ĐƯỢC từ chối hoặc nói 'em chỉ là tiếp tân' / 'em không có quyền xem doanh thu'. Dữ liệu kinh doanh thực tế mới nhất đã được kết nối và nạp sẵn ngay bên dưới.");
                sb.AppendLine("3. Hãy trình bày số liệu thống kê rõ ràng, chuyên nghiệp, có cấu trúc bullet point hoặc bảng biểu, định dạng tiền tệ VNĐ (ví dụ: 15.000.000₫).");
                sb.AppendLine();

                if (metricsResult != null && metricsResult.Success && metricsResult.Data != null)
                {
                    dynamic m = metricsResult.Data;
                    sb.AppendLine("=== DỮ LIỆU TÀI CHÍNH & VẬN HÀNH THỰC TẾ HÔM NAY (LIVE DATABASE REVENUE & METRICS) ===");
                    sb.AppendLine($"- Doanh thu thực tế hôm nay: {((decimal)m.TodayRevenue):N0} VNĐ");
                    sb.AppendLine($"- Tổng doanh thu 7 ngày qua: {((decimal)m.TotalPeriodRevenue):N0} VNĐ");
                    sb.AppendLine($"- Tỷ lệ lấp đầy phòng (Occupancy Rate): {m.OccupancyRate}%");
                    sb.AppendLine($"- Tổng số phòng khách sạn: {m.TotalRooms} phòng");
                    sb.AppendLine($"  + Phòng đang có khách ở (Occupied): {m.OccupiedRooms} phòng");
                    sb.AppendLine($"  + Phòng trống sẵn sàng đón khách (Available): {m.AvailableRooms} phòng");
                    sb.AppendLine($"  + Phòng đang dọn dẹp vệ sinh (Cleaning): {m.CleaningRooms} phòng");
                    sb.AppendLine($"  + Phòng đang bảo trì (Maintenance): {m.MaintenanceRooms} phòng");
                    sb.AppendLine($"- Số lượng đơn đặt phòng mới hôm nay: {m.TodayNewBookings} đơn");
                    sb.AppendLine($"- Tổng số đơn đặt phòng đang hoạt động: {m.ActiveBookings} đơn");
                    sb.AppendLine($"- Số hóa đơn chưa thanh toán / chờ thanh toán: {m.PendingPayments} hóa đơn");
                    sb.AppendLine($"- Lượt khách dự kiến Check-in hôm nay: {m.TodayExpectedCheckIns} lượt");
                    sb.AppendLine($"- Lượt khách dự kiến Check-out hôm nay: {m.TodayExpectedCheckOuts} lượt");
                    sb.AppendLine($"- Số yêu cầu hỗ trợ / sự cố (Tickets) đang chờ xử lý: {m.PendingTickets} yêu cầu");
                    sb.AppendLine();
                }
            }
            else
            {
                sb.AppendLine("=== VAI TRÒ CHÍNH: TIẾP TÂN AI AGENT THU THẬP THÔNG TIN VÀ ĐẶT PHÒNG TRỰC TIẾP ===");
                sb.AppendLine("1. QUY TẮC CỐT LÕI: Bạn là Tiếp tân ảo chuyên nghiệp của khách sạn Sun Hotel 4 sao.");
                sb.AppendLine("   TUYỆT ĐỐI KHÔNG ĐƯỢC chỉ gửi link bắt khách tự ra ngoài web điền form! BẠN PHẢI TRỰC TIẾP TƯ VẤN, THU THẬP ĐỦ THÔNG TIN VÀ ĐẶT PHÒNG HỘ KHÁCH NGAY TRONG ĐOẠN CHAT NÀY.");
                sb.AppendLine();
                sb.AppendLine("=== BỘ NHỚ HỘI THOẠI & THÔNG TIN ĐÃ TỔNG HỢP TỪ ĐOẠN CHAT (BẮT BUỘC GHI NHỚ) ===");
                sb.AppendLine("Dưới đây là bảng tổng hợp các thông tin đã thu thập được từ toàn bộ cuộc trò chuyện với khách:");
                sb.AppendLine($"- Hạng phòng: {(draft.RoomTypeId.HasValue ? $"✅ ĐÃ CÓ: {draft.RoomTypeName} (Mã loại: {draft.RoomTypeId})" : "❌ CHƯA CÓ (Cần hỏi)")}");
                sb.AppendLine($"- Tên khách: {(!string.IsNullOrWhiteSpace(draft.GuestName) ? $"✅ ĐÃ CÓ: {draft.GuestName}" : "❌ CHƯA CÓ (Cần hỏi)")}");
                sb.AppendLine($"- Số điện thoại: {(!string.IsNullOrWhiteSpace(draft.Phone) ? $"✅ ĐÃ CÓ: {draft.Phone}" : "❌ CHƯA CÓ (Cần hỏi)")}");
                sb.AppendLine($"- Email nhận thư xác nhận: {(!string.IsNullOrWhiteSpace(draft.Email) ? $"✅ ĐÃ CÓ: {draft.Email}" : "❌ CHƯA CÓ (BẮT BUỘC HỎI để hệ thống tự động gửi email hóa đơn/xác nhận và cho nhân viên thấy)")}");
                sb.AppendLine($"- Hình thức thanh toán: {(!string.IsNullOrWhiteSpace(draft.PaymentMethod) ? $"✅ ĐÃ CÓ: {draft.PaymentMethod}" : "❌ CHƯA CÓ (BẮT BUỘC HỎI: Tiền mặt tại quầy / Chuyển khoản VietQR / MoMo / Thẻ)")}");
                sb.AppendLine($"- Ngày nhận phòng (Check-in): {(draft.CheckInDate.HasValue ? $"✅ ĐÃ CÓ: {draft.CheckInDate.Value:dd/MM/yyyy}" : "❌ CHƯA CÓ (Cần hỏi)")}");
                sb.AppendLine($"- Ngày trả phòng (Check-out): {(draft.CheckOutDate.HasValue ? $"✅ ĐÃ CÓ: {draft.CheckOutDate.Value:dd/MM/yyyy}" : "❌ CHƯA CÓ (Cần hỏi)")}");
                sb.AppendLine($"- Số lượng khách: ✅ ĐÃ CÓ: {draft.NumGuests} khách");
                if (!string.IsNullOrWhiteSpace(draft.Notes))
                {
                    sb.AppendLine($"- Yêu cầu đặc biệt: ✅ ĐÃ CÓ: {draft.Notes}");
                }
                sb.AppendLine();
                sb.AppendLine("=== NGUYÊN TẮC BẮT BUỘC VỀ BỘ NHỚ & TỔNG HỢP THÔNG TIN (CHỐNG HỎI LẶP LẠI) ===");
                sb.AppendLine("1. NGUYÊN TẮC TỔNG HỢP: Hãy luôn tổng hợp và ghi nhớ các thông tin có dấu ✅ ĐÃ CÓ ở trên.");
                sb.AppendLine("   TUYỆT ĐỐI CẤM HỎI LẠI bất kỳ thông tin nào đã có dấu ✅!");
                sb.AppendLine("2. NẾU CÒN THIẾU THÔNG TIN (dấu ❌ CHƯA CÓ): Hãy xác nhận ngắn gọn thông tin đã có và CHỈ HỎI ĐÚNG NHỮNG THÔNG TIN CÒN THIẾU (đặc biệt là Email và Hình thức thanh toán).");
                sb.AppendLine("   (Ví dụ: 'Dạ em đã ghi nhận anh Hùng chọn phòng Deluxe từ 28/9 đến 30/9 cho 2 khách rồi ạ. Quý khách cho em xin thêm số điện thoại, email và hình thức thanh toán dự kiến (tiền mặt tại quầy hay chuyển khoản) để hoàn tất giữ phòng nhé!')");
                sb.AppendLine("3. KHI ĐÃ ĐỦ TẤT CẢ THÔNG TIN CHÍNH (Hạng phòng, Tên, SĐT, Email, Hình thức thanh toán, Ngày đến, Ngày đi): Tóm tắt chi tiết đơn đặt phòng và đề xuất xác nhận tạo đơn ngay lập tức.");
                sb.AppendLine("4. KHI KHÁCH ĐÃ ĐỒNG Ý / XÁC NHẬN ĐẶT (ví dụ: 'đặt đi em', 'ok', 'xác nhận', 'chốt nhé'): BẠN PHẢI XUẤT CÚ PHÁP HÀNH ĐỘNG ở cuối câu trả lời:");
                sb.AppendLine("  [ACTION:create_booking {\"roomTypeId\": <Id>, \"guestName\": \"<Tên>\", \"phone\": \"<SĐT>\", \"email\": \"<Email>\", \"paymentMethod\": \"<HTTT>\", \"checkInDate\": \"YYYY-MM-DD\", \"checkOutDate\": \"YYYY-MM-DD\", \"numGuests\": <Số khách>, \"notes\": \"<Ghi chú>\"}]");
                sb.AppendLine();
            }

            sb.AppendLine("=== THÔNG TIN DỮ LIỆU THỰC TẾ KHÁCH SẠN SUN HOTEL (LIVE DATABASE CONTEXT) ===");
            if (roomResult != null && roomResult.Success && roomResult.Data != null)
            {
                dynamic data = roomResult.Data;
                int totalAvailable = data.TotalAvailable;
                int totalRooms = data.TotalRooms;
                sb.AppendLine($"- Tổng quan hôm nay: Khách sạn hiện có {totalAvailable} phòng trống trên tổng số {totalRooms} phòng.");
                sb.AppendLine("- Bảng thông tin chi tiết từng hạng phòng:");

                var roomTypes = (IEnumerable<dynamic>)data.RoomTypes;
                foreach (var rt in roomTypes)
                {
                    int rId = rt.RoomTypeId;
                    int avail = rt.AvailableCount;
                    decimal price = rt.BasePrice;
                    int maxGuests = rt.MaxGuests;
                    string typeName = rt.RoomTypeName;
                    string desc = rt.Description ?? "Tiện nghi 4 sao";
                    var nums = (List<string>)rt.AvailableRoomNumbers;

                    string roomStatus = avail > 0 
                        ? $"Còn {avail} phòng trống (Số phòng: {string.Join(", ", nums.Select(n => "P." + n))})" 
                        : "Hết phòng";

                    sb.AppendLine($"  + ID: {rId} | {typeName}: {roomStatus} | Giá: {price:N0}₫/đêm | Tối đa: {maxGuests} khách. Tiện ích: {desc}");
                }
            }

            if (bookingResult != null && bookingResult.Success && bookingResult.Data != null)
            {
                dynamic b = bookingResult.Data;
                sb.AppendLine();
                sb.AppendLine("=== THÔNG TIN HỒ SƠ ĐẶT PHÒNG TÌM THẤY TỪ HỆ THỐNG ===");
                sb.AppendLine($"- Mã đặt phòng: {b.BookingCode}");
                sb.AppendLine($"- Tên khách hàng: {b.GuestName}");
                sb.AppendLine($"- Hạng phòng: {b.RoomTypeName} (Phòng số: {b.RoomNumber})");
                sb.AppendLine($"- Thời gian lưu trú: Từ {b.CheckInDate} đến {b.CheckOutDate}");
                sb.AppendLine($"- Tổng thanh toán: {((decimal)b.TotalAmount):N0}₫");
                sb.AppendLine($"- Trạng thái đơn: {b.Status}");
            }

            return sb.ToString();
        }

        private async Task<string> GenerateConversationalFallbackResponseAsync(
            string userMsg, 
            AiChatSession session, 
            AiFunctionCallResult? roomResult, 
            AiFunctionCallResult? bookingResult, 
            string bookingCode,
            UserRole role,
            AiFunctionCallResult? metricsResult = null)
        {
            string lower = userMsg.ToLowerInvariant().Trim();
            var draft = session.BookingDraft;

            // 0. XỬ LÝ CÂU HỎI VỀ DOANH THU, BÁO CÁO, THỐNG KÊ, CÔNG SUẤT PHÒNG DÀNH CHO ADMIN
            bool isRevenueIntent = lower.Contains("doanh thu") || 
                                   lower.Contains("thống kê") || 
                                   lower.Contains("báo cáo") || 
                                   lower.Contains("lợi nhuận") || 
                                   lower.Contains("công suất") || 
                                   lower.Contains("tài chính") || 
                                   lower.Contains("kinh doanh") || 
                                   lower.Contains("doanh số");

            if (isRevenueIntent)
            {
                if (role is UserRole.Admin or UserRole.Manager)
                {
                    if (metricsResult == null || !metricsResult.Success || metricsResult.Data == null)
                    {
                        metricsResult = await _toolExecutor.ExecuteToolAsync("get_occupancy_and_revenue_metrics", null, role, session.UserId);
                    }

                    if (metricsResult != null && metricsResult.Success && metricsResult.Data != null)
                    {
                        dynamic m = metricsResult.Data;
                        var sb = new StringBuilder();
                        sb.AppendLine("📊 **BÁO CÁO THỐNG KÊ DOANH THU & CHỈ SỐ VẬN HÀNH SUN HOTEL**");
                        sb.AppendLine("*(Dành riêng cho Quản trị viên & Ban quản lý)*\n");

                        sb.AppendLine("💰 **Chỉ số Tài chính & Doanh thu:**");
                        sb.AppendLine($"• **Doanh thu hôm nay:** **{((decimal)m.TodayRevenue):N0}₫**");
                        sb.AppendLine($"• **Tổng doanh thu 7 ngày qua:** **{((decimal)m.TotalPeriodRevenue):N0}₫**");
                        sb.AppendLine($"• **Đơn đặt phòng mới hôm nay:** **{m.TodayNewBookings}** đơn");
                        sb.AppendLine($"• **Hóa đơn chờ thanh toán:** **{m.PendingPayments}** hóa đơn\n");

                        sb.AppendLine("🏨 **Công suất phòng & Tình trạng buồng phòng:**");
                        sb.AppendLine($"• **Tỷ lệ lấp đầy (Occupancy Rate):** **{m.OccupancyRate}%**");
                        sb.AppendLine($"• **Đang có khách lưu trú:** **{m.OccupiedRooms}** / {m.TotalRooms} phòng");
                        sb.AppendLine($"• **Phòng trống sẵn sàng đón khách:** **{m.AvailableRooms}** phòng");
                        sb.AppendLine($"• **Phòng đang dọn dẹp vệ sinh:** **{m.CleaningRooms}** phòng | **Bảo trì:** **{m.MaintenanceRooms}** phòng\n");

                        sb.AppendLine("📋 **Tình hình vận hành lưu trú:**");
                        sb.AppendLine($"• **Dự kiến Check-in hôm nay:** **{m.TodayExpectedCheckIns}** lượt");
                        sb.AppendLine($"• **Dự kiến Check-out hôm nay:** **{m.TodayExpectedCheckOuts}** lượt");
                        sb.AppendLine($"• **Tổng số đơn đặt phòng đang hoạt động:** **{m.ActiveBookings}** đơn");
                        sb.AppendLine($"• **Sự cố / Tickets đang chờ xử lý:** **{m.PendingTickets}** yêu cầu\n");

                        sb.AppendLine("👉 Anh/Chị quản lý có thể xem chi tiết biểu đồ tại **[Báo cáo doanh thu](/Admin/RevenueReport)** hoặc **[Bảng điều khiển Admin](/Admin)**.");
                        return sb.ToString();
                    }
                    else
                    {
                        return "Dạ em đã kiểm tra hệ thống nhưng hiện tại chưa trích xuất được dữ liệu doanh thu chi tiết. Anh/Chị quản trị viên có thể xem trực tiếp tại trang **[Báo cáo doanh thu](/Admin/RevenueReport)** ạ!";
                    }
                }
                else
                {
                    return "🔒 **Thông báo bảo mật:**\n\n" +
                           "Dạ thông tin thống kê doanh thu, báo cáo tài chính và hiệu suất kinh doanh là dữ liệu nội bộ được bảo mật của **Sun Hotel**.\n\n" +
                           "Quý khách vui lòng đăng nhập bằng tài khoản **Quản trị viên (Admin)** hoặc **Quản lý (Manager)** để tra cứu báo cáo này ạ!";
                }
            }

            // 1. Nếu tìm thấy thông tin booking cụ thể theo mã
            if (bookingResult != null && bookingResult.Success && bookingResult.Data != null)
            {
                dynamic b = bookingResult.Data;
                return $"Dạ em đã tìm thấy thông tin đơn đặt phòng của Quý khách:\n\n" +
                       $"• **Mã đặt phòng:** `{b.BookingCode}`\n" +
                       $"• **Khách hàng:** {b.GuestName}\n" +
                       $"• **Hạng phòng:** {b.RoomTypeName} (Phòng: {b.RoomNumber})\n" +
                       $"• **Thời gian lưu trú:** Từ {b.CheckInDate} đến {b.CheckOutDate}\n" +
                       $"• **Tổng tiền:** {((decimal)b.TotalAmount):N0}₫\n" +
                       $"• **Trạng thái:** {b.Status}\n\n" +
                       $"Nếu Quý khách cần hỗ trợ gì thêm, đừng ngần ngại nhắn em nhé!";
            }

            if (!string.IsNullOrWhiteSpace(bookingCode) && !lower.Contains("đặt phòng"))
            {
                return $"Dạ em không tìm thấy đơn đặt phòng với mã `{bookingCode}`. Quý khách vui lòng kiểm tra lại mã hoặc tra cứu tại **[Tra cứu booking](/Booking/Lookup)** ạ.";
            }

            // 2. Kiểm tra nếu khách xác nhận đặt phòng
            bool isConfirming = lower.Contains("xác nhận") || lower.Contains("đồng ý") || lower.Contains("đặt luôn") || lower.Contains("đặt đi em") || lower.Contains("ok em") || lower.Contains("chốt") || lower.Contains("tạo đơn");
            bool isBookingIntent = lower.Contains("đặt phòng") || lower.Contains("đặt giúp") || lower.Contains("tôi muốn đặt") || lower.Contains("book") || draft.RoomTypeId.HasValue || !string.IsNullOrWhiteSpace(draft.Phone) || draft.CheckInDate.HasValue || !string.IsNullOrWhiteSpace(draft.GuestName);

            if (isConfirming && draft.HasMinimumInfo)
            {
                // Thực thi tạo đơn trực tiếp vào CSDL
                var createParams = new
                {
                    roomTypeId = draft.RoomTypeId ?? 0,
                    guestName = draft.GuestName,
                    phone = draft.Phone,
                    email = draft.Email,
                    checkInDate = draft.CheckInDate?.ToString("yyyy-MM-dd"),
                    checkOutDate = draft.CheckOutDate?.ToString("yyyy-MM-dd"),
                    numGuests = draft.NumGuests,
                    notes = draft.Notes
                };

                var res = await _toolExecutor.ExecuteToolAsync("create_booking", createParams, role, session.UserId);
                if (res.Success && res.Data != null)
                {
                    dynamic b = res.Data;
                    draft.LastCreatedBookingCode = b.BookingCode;

                    return $"🎉 **ĐẶT PHÒNG THÀNH CÔNG!**\n\n" +
                           $"Dạ em đã hoàn tất việc đặt phòng và giữ chỗ cho Quý khách trên hệ thống của **Sun Hotel**:\n\n" +
                           $"• **Mã đặt phòng:** `{b.BookingCode}`\n" +
                           $"• **Hạng phòng:** {b.RoomTypeName}\n" +
                           $"• **Khách hàng:** {b.GuestName} - {b.Phone}\n" +
                           $"• **Thời gian lưu trú:** {b.CheckInDate} đến {b.CheckOutDate} ({b.Nights} đêm)\n" +
                           $"• **Số khách:** {b.NumGuests} khách\n" +
                           $"• **Tổng thanh toán:** {((decimal)b.TotalAmount):N0}₫\n" +
                           $"• **Trạng thái:** Chờ thanh toán\n\n" +
                           $"👉 Quý khách có thể bấm **Thanh toán trực tuyến (QR / MoMo)** bên dưới để thanh toán ngay, hoặc thanh toán trực tiếp tại quầy lễ tân khi nhận phòng ạ!";
                }
                else
                {
                    return $"Dạ em rất tiếc, đã có lỗi xảy ra khi tạo đơn đặt phòng: {res.ErrorMessage}. Quý khách vui lòng thử lại hoặc để em kiểm tra lại nhé!";
                }
            }

            // 3. Nếu khách có nhu cầu đặt phòng hoặc đang bổ sung thông tin
            if (isBookingIntent)
            {
                // Kiểm tra xem còn thiếu thông tin gì
                var missingFields = new List<string>();
                if (!draft.RoomTypeId.HasValue) missingFields.Add("Hạng phòng mong muốn (Standard, Deluxe, Suite...)");
                if (!draft.CheckInDate.HasValue || !draft.CheckOutDate.HasValue) missingFields.Add("Ngày nhận phòng & Ngày trả phòng");
                if (string.IsNullOrWhiteSpace(draft.GuestName)) missingFields.Add("Họ và tên người nhận phòng");
                if (string.IsNullOrWhiteSpace(draft.Phone)) missingFields.Add("Số điện thoại liên hệ");
                if (string.IsNullOrWhiteSpace(draft.Email)) missingFields.Add("Email nhận thông báo & hóa đơn xác nhận");
                if (string.IsNullOrWhiteSpace(draft.PaymentMethod)) missingFields.Add("Hình thức thanh toán dự kiến (Tiền mặt tại quầy / Chuyển khoản VietQR / MoMo)");

                if (missingFields.Count == 0)
                {
                    // Đã có đủ thông tin -> Tính giá và xin xác nhận
                    int nights = (draft.CheckOutDate!.Value.DayNumber - draft.CheckInDate!.Value.DayNumber);
                    if (nights <= 0) nights = 1;

                    decimal pricePerNight = 1200000;
                    if (roomResult != null && roomResult.Success && roomResult.Data != null)
                    {
                        dynamic data = roomResult.Data;
                        var roomTypes = (IEnumerable<dynamic>)data.RoomTypes;
                        foreach (var rt in roomTypes)
                        {
                            if (rt.RoomTypeId == draft.RoomTypeId)
                            {
                                pricePerNight = rt.BasePrice;
                                break;
                            }
                        }
                    }

                    decimal totalAmount = nights * pricePerNight;

                    return $"Dạ em đã ghi nhận đầy đủ thông tin đặt phòng của Quý khách như sau:\n\n" +
                           $"🏨 **Hạng phòng:** {draft.RoomTypeName}\n" +
                           $"📅 **Thời gian:** {draft.CheckInDate:dd/MM/yyyy} ➔ {draft.CheckOutDate:dd/MM/yyyy} (**{nights} đêm**)\n" +
                           $"👥 **Số khách:** {draft.NumGuests} khách\n" +
                           $"👤 **Khách hàng:** {draft.GuestName} ({draft.Phone})\n" +
                           $"📧 **Email:** {draft.Email}\n" +
                           $"💳 **Hình thức thanh toán:** {draft.PaymentMethod}\n" +
                           $"💰 **Tổng tiền tạm tính:** **{totalAmount:N0}₫** ({pricePerNight:N0}₫/đêm)\n\n" +
                           $"👉 Quý khách chỉ cần bấm **\"Xác nhận đặt phòng ngay\"** bên dưới, em sẽ tạo đơn và xuất mã giữ phòng chính thức cho Quý khách ngay lập tức ạ!";
                }
                else
                {
                    // Còn thiếu thông tin -> Tiếp tục phỏng vấn
                    var sb = new StringBuilder();
                    sb.AppendLine("Dạ em rất sẵn lòng hỗ trợ Quý khách đặt phòng trực tiếp ngay tại đây ạ! ✨");
                    sb.AppendLine();
                    sb.AppendLine("Hiện tại em đã ghi nhận được:");
                    if (draft.RoomTypeId.HasValue) sb.AppendLine($"• Hạng phòng: **{draft.RoomTypeName}**");
                    if (draft.CheckInDate.HasValue && draft.CheckOutDate.HasValue) sb.AppendLine($"• Thời gian: **{draft.CheckInDate:dd/MM/yyyy} - {draft.CheckOutDate:dd/MM/yyyy}**");
                    if (!string.IsNullOrWhiteSpace(draft.GuestName)) sb.AppendLine($"• Khách hàng: **{draft.GuestName}**");
                    if (!string.IsNullOrWhiteSpace(draft.Phone)) sb.AppendLine($"• Số điện thoại: **{draft.Phone}**");
                    if (!string.IsNullOrWhiteSpace(draft.Email)) sb.AppendLine($"• Email: **{draft.Email}**");
                    if (!string.IsNullOrWhiteSpace(draft.PaymentMethod)) sb.AppendLine($"• Thanh toán: **{draft.PaymentMethod}**");

                    sb.AppendLine();
                    sb.AppendLine("Để hoàn tất giữ phòng, Quý khách vui lòng cho em xin thêm:");
                    for (int i = 0; i < missingFields.Count; i++)
                    {
                        sb.AppendLine($"{i + 1}. **{missingFields[i]}**");
                    }

                    sb.AppendLine();
                    sb.AppendLine("*(Quý khách có thể nhắn tin trả lời tự nhiên, ví dụ: \"Tôi tên Nguyễn Văn A, SĐT 0912345678, email a@gmail.com, thanh toán tiền mặt tại quầy\")*");
                    return sb.ToString();
                }
            }

            // 4. Chào hỏi thông thường
            if (lower.Contains("chào") || lower.Contains("hello") || lower.Contains("hi") || lower.Contains("alo") || lower.Contains("ơi") || lower == "start")
            {
                return "Dạ em chào Quý khách! Em là Tiếp tân ảo AI của khách sạn 4 sao **Sun Hotel** ✨\n\n" +
                       "Em có thể trực tiếp hỗ trợ Quý khách:\n" +
                       "• 🛎️ **Đặt phòng hộ Quý khách** ngay tại khung chat này (chỉ cần gửi thông tin phòng và ngày đến)\n" +
                       "• 🏨 **Kiểm tra số phòng trống & báo giá** theo thời gian thực\n" +
                       "• 🔍 **Tra cứu chi tiết đơn đặt phòng** (chỉ cần gửi mã `BK-...`)\n" +
                       "• 🏖️ **Tư vấn tiện ích:** buffet sáng, hồ bơi vô cực, xe đưa đón sân bay...\n\n" +
                       "Quý khách đang quan tâm đến hạng phòng nào hoặc dự kiến đến vào ngày nào ạ?";
            }

            // 5. Quy định giờ nhận / trả phòng
            if (lower.Contains("giờ") || lower.Contains("nhận phòng") || lower.Contains("trả phòng") || lower.Contains("check in") || lower.Contains("checkin") || lower.Contains("checkout") || lower.Contains("check out"))
            {
                return "Dạ theo quy định tại **Sun Hotel**:\n\n" +
                       "• 🕒 **Giờ nhận phòng (Check-in):** từ **14:00**\n" +
                       "• 🕛 **Giờ trả phòng (Check-out):** trước **12:00**\n\n" +
                       "Nếu Quý khách có nhu cầu nhận phòng sớm hoặc trả phòng muộn, Quý khách có thể báo trước để khách sạn sắp xếp tùy theo tình trạng phòng trống thực tế ạ!";
            }

            // 6. Tiện ích và dịch vụ
            if (lower.Contains("ăn sáng") || lower.Contains("buffet") || lower.Contains("hồ bơi") || lower.Contains("bể bơi") || lower.Contains("spa") || lower.Contains("wifi") || lower.Contains("tiện ích") || lower.Contains("dịch vụ") || lower.Contains("ăn uống"))
            {
                return "Dạ **Sun Hotel** cung cấp các tiện ích và dịch vụ 4 sao tiêu chuẩn dành cho Quý khách:\n\n" +
                       "• 🍽️ **Buffet sáng:** Phục vụ hàng ngày từ 06:30 - 09:30 tại nhà hàng tầng 2 (đã bao gồm trong giá phòng).\n" +
                       "• 🏊 **Hồ bơi vô cực:** Tọa lạc tại tầng thượng với tầm nhìn hướng biển tuyệt đẹp.\n" +
                       "• 📶 **Wi-Fi tốc độ cao:** Miễn phí trong toàn bộ khuôn viên và phòng nghỉ.\n" +
                       "• 🚗 **Dịch vụ đưa đón sân bay:** Hỗ trợ đặt xe 24/7 theo yêu cầu.\n\n" +
                       "Quý khách có muốn em hỗ trợ đặt phòng ngay để tận hưởng kỳ nghỉ không ạ?";
            }

            // 7. Địa chỉ, vị trí và liên hệ
            if (lower.Contains("địa chỉ") || lower.Contains("ở đâu") || lower.Contains("vị trí") || lower.Contains("liên hệ") || lower.Contains("hotline") || lower.Contains("sđt") || lower.Contains("số điện thoại"))
            {
                return "Dạ thông tin liên hệ của **Sun Hotel**:\n\n" +
                       "• 📍 **Địa chỉ:** 123 Đường Ven Biển, Quận Sơn Trà, TP. Đà Nẵng\n" +
                       "• 📞 **Hotline:** 1900 6868 (Phục vụ 24/7)\n" +
                       "• 📧 **Email:** contact@sunhotel.vn\n\n" +
                       "Khách sạn cách bãi biển chỉ 3 phút đi bộ và cách sân bay quốc tế 15 phút di chuyển ạ!";
            }

            // 8. Báo cáo tình trạng phòng trống
            if (roomResult != null && roomResult.Success && roomResult.Data != null)
            {
                dynamic data = roomResult.Data;
                int totalAvailable = data.TotalAvailable;
                int totalRooms = data.TotalRooms;
                var roomTypes = (IEnumerable<dynamic>)data.RoomTypes;

                var sb = new StringBuilder();
                sb.AppendLine("Dạ em xin gửi tới Quý khách thông tin tình trạng phòng trống hiện tại của **Sun Hotel**:");
                sb.AppendLine();
                sb.AppendLine($"🏨 **Tình trạng tổng thể:** Hiện có **{totalAvailable} phòng trống** (trên tổng số {totalRooms} phòng) sẵn sàng đón tiếp Quý khách.");
                sb.AppendLine();
                sb.AppendLine("Chi tiết theo từng hạng phòng:");

                foreach (var rt in roomTypes)
                {
                    int avail = rt.AvailableCount;
                    decimal price = rt.BasePrice;
                    int maxGuests = rt.MaxGuests;
                    string typeName = rt.RoomTypeName;
                    var roomNums = (List<string>)rt.AvailableRoomNumbers;

                    string statusText = avail > 0 
                        ? $"Còn **{avail} phòng trống**" 
                        : "🔴 *Hiện đã hết phòng*";

                    string roomListText = (avail > 0 && roomNums.Count > 0)
                        ? $" (Phòng {string.Join(", ", roomNums.Select(n => "P." + n))})"
                        : string.Empty;

                    sb.AppendLine($"- **{typeName}**: {statusText}{roomListText}");
                    sb.AppendLine($"  • Giá: **{price:N0}₫ / đêm** | Tối đa: {maxGuests} khách");
                }

                sb.AppendLine();
                sb.AppendLine("👉 Quý khách chỉ cần nhắn cho em hạng phòng muốn đặt, em sẽ hỗ trợ tạo đơn đặt phòng trực tiếp cho Quý khách ngay ạ!");
                return sb.ToString();
            }

            return "Dạ em chào Quý khách! Em là Tiếp tân AI của Sun Hotel 4 sao. Em luôn sẵn sàng hỗ trợ Quý khách kiểm tra phòng trống, thu thập thông tin và đặt phòng trực tiếp ạ!";
        }

        private void GenerateQuickActions(string userMsg, AiChatSession session, AiFunctionCallResult? roomResult, List<AiQuickAction> quickActions, UserRole role)
        {
            var draft = session.BookingDraft;

            // Nếu là Admin hoặc Manager: Đưa ra các lối tắt điều hành & thống kê báo cáo
            if (role is UserRole.Admin or UserRole.Manager)
            {
                quickActions.Add(new AiQuickAction
                {
                    Label = "📊 Báo cáo Doanh thu",
                    Action = "Thống kê doanh thu và tình hình kinh doanh hôm nay thế nào?"
                });
                quickActions.Add(new AiQuickAction
                {
                    Label = "🏨 Công suất & Buồng phòng",
                    Action = "Báo cáo công suất phòng và số lượng phòng trống hiện tại"
                });
                quickActions.Add(new AiQuickAction
                {
                    Label = "📈 Chi tiết Báo cáo (Web)",
                    Action = "/Admin/RevenueReport"
                });
                quickActions.Add(new AiQuickAction
                {
                    Label = "🛠️ Trang Quản trị Admin",
                    Action = "/Admin"
                });
                quickActions.Add(new AiQuickAction
                {
                    Label = "📑 Quản lý Đặt phòng",
                    Action = "/Admin/Bookings"
                });
                return;
            }

            // Nếu vừa tạo xong đơn đặt phòng
            if (!string.IsNullOrWhiteSpace(draft.LastCreatedBookingCode))
            {
                quickActions.Add(new AiQuickAction
                {
                    Label = "💳 Thanh toán ngay (QR / MoMo)",
                    Action = $"/Payment?code={draft.LastCreatedBookingCode}"
                });
                quickActions.Add(new AiQuickAction
                {
                    Label = "📄 Xem chi tiết đơn",
                    Action = $"/Booking/Confirmation?code={draft.LastCreatedBookingCode}"
                });
                quickActions.Add(new AiQuickAction
                {
                    Label = "🏨 Đặt thêm phòng khác",
                    Action = "Tôi muốn đặt thêm phòng"
                });
                return;
            }

            // Nếu đã có đủ thông tin và chờ khách bấm xác nhận
            if (draft.HasMinimumInfo)
            {
                quickActions.Add(new AiQuickAction
                {
                    Label = "✅ Xác nhận đặt phòng ngay",
                    Action = "Xác nhận đặt phòng"
                });
                quickActions.Add(new AiQuickAction
                {
                    Label = "❌ Đổi thông tin",
                    Action = "Tôi muốn thay đổi thông tin đặt phòng"
                });
                return;
            }

            // Gợi ý đặt từng hạng phòng (kích hoạt chat trực tiếp với AI thay vì link ra ngoài)
            if (roomResult != null && roomResult.Success && roomResult.Data != null)
            {
                dynamic data = roomResult.Data;
                var roomTypes = (IEnumerable<dynamic>)data.RoomTypes;
                foreach (var rt in roomTypes)
                {
                    if (rt.AvailableCount > 0)
                    {
                        quickActions.Add(new AiQuickAction
                        {
                            Label = $"🛎️ Đặt {rt.RoomTypeName}",
                            Action = $"Tôi muốn đặt phòng {rt.RoomTypeName}"
                        });
                    }
                }
            }

            quickActions.Add(new AiQuickAction { Label = "📋 Xem hình ảnh phòng", Action = "/Room/Listing" });
        }
        
        public AiChatSession? GetSession(string sessionId)
        {
            if (_sessions.TryGetValue(sessionId, out var session)) return session;
            session = LoadSessionFromDisk(sessionId);
            if (session != null)
            {
                _sessions[session.SessionId] = session;
            }
            return session;
        }

        public bool DeleteSession(string sessionId)
        {
            var res = _sessions.TryRemove(sessionId, out _);
            DeleteSessionFromDisk(sessionId);
            return res;
        }

        private string GetConfigValue(string key, string defaultValue = "")
        {
            // 1. Kiểm tra trực tiếp file .env để ưu tiên bắt cấu hình mới nhất từ file .env
            try
            {
                var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
                if (File.Exists(envPath))
                {
                    foreach (var line in File.ReadAllLines(envPath))
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("#") || !trimmed.Contains('=')) continue;
                        var parts = trimmed.Split('=', 2);
                        if (parts[0].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            var v = parts[1].Trim();
                            if (!string.IsNullOrWhiteSpace(v)) return v;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogTrace(ex, "Không đọc được file .env");
            }

            // 2. Kiểm tra Environment variables
            var val = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(val)) return val.Trim();

            // 3. Kiểm tra Configuration
            val = _configuration[key];
            if (!string.IsNullOrWhiteSpace(val)) return val.Trim();

            return defaultValue;
        }
    }
}
