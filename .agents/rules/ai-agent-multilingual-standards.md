# AI CONCIERGE & MULTILINGUAL STANDARDS

## 1. DUAL ARCHITECTURE: LLM + CONVERSATIONAL FALLBACK
- The AI Agent (`AiChatService`) operates on a **Dual Engine**:
  1. **Primary LLM:** Calls Google Gemini API (`gemini-2.5-flash`, `gemini-2.0-flash`, `gemini-1.5-flash`).
  2. **Conversational Fallback Engine:** If the API key is not configured, expired, or network requests time out, the system automatically falls back to `GenerateConversationalFallbackResponseAsync`.
- **Absolute Rule:** The fallback engine MUST be as articulate, helpful, and high-quality as the LLM. It must NEVER display generic error strings like "System offline" or "Error communicating with AI".

## 2. MULTILINGUAL FIRST PRINCIPLE
- The hotel hosts both Vietnamese and international guests.
- **Language Detection:**
  - Utilize `IsEnglishOrForeign(text, history)` to detect non-Vietnamese interactions.
  - Automatically conduct greetings, room recommendations, price quotes, booking field collection, confirmations, and rejections in the guest's language.
- **Price Transparency:**
  - When communicating in English / foreign languages, provide prices in Vietnamese Dong accompanied by approximate USD (`450,000 VND (~$18.0 USD)` based on standard ~25,000 VND/USD rate).
- **Frontend Widget Adaptation:**
  - `ai-chat-widget.js` detects `navigator.language`. For non-Vietnamese browsers, initialize English starter chips and placeholders automatically.

## 3. STRICT ENTITY EXTRACTION & ZERO FALSE POSITIVES
- **Booking Codes:**
  - A booking code MUST match prefixes like `BK...` (`\bBK-?[A-Za-z0-9]{4,16}\b`) or follow explicit keywords (`(?:mã|code|booking code)\s*[:#]?\s*([A-Za-z0-9\-]{4,16})`).
  - NEVER treat common conversational words (e.g., `available`, `tomorrow`, `reservation`, `standard`) as booking codes.
- **Phone Numbers:**
  - Support both domestic Vietnamese numbers (`09...`, `03...`, 10 digits) and international numbers starting with `+` (`+\d{1,4}\s?\d{6,14}`).
- **Guest Names & Dates:**
  - Parse English date expressions (`today`, `tomorrow`, `tonight`, `from ... to ...`, `X nights`).
  - Accept international guest names without forcing Vietnamese naming structures.

## 4. GEMINI MODEL CONFIGURATION
- Use valid, supported model identifiers:
  - `gemini-2.5-flash` (recommended default for speed and reasoning)
  - `gemini-2.0-flash`
  - `gemini-1.5-flash`
  - `gemini-2.5-pro`
- NEVER hallucinate unreleased model names (e.g., `gemini-3.8-flash`).

## 5. REVENUE & INTERNAL DATA DEFENSE
- Queries regarding operational revenue, occupancy rates, financial reports, or staff analytics must check the caller's role.
- If the caller is not `Admin` or `Manager`, reject the query with a polite security notice explaining that operational data is confidential.
