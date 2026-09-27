/**
 * AI Chat Widget for Hotel Management System
 * Requires ai-chat-widget.css
 */
(function() {
    'use strict';

    const CONFIG = {
        apiEndpoint: '/api/ai/chat',
        streamEndpoint: '/api/ai/chat/stream',
        colors: {
            primary: '#1a5276',
            accent: '#d4af37'
        },
        storageKey: 'hotelAiChatSession',
        welcomeMessage: 'Chào bạn, tôi có thể giúp gì cho bạn hôm nay?',
        botName: 'Trợ lý Khách sạn'
    };

    let sessionData = {
        sessionId: null,
        history: [],
        isOpen: false
    };

    // DOM Elements
    let elements = {};
    let isWaitingForResponse = false;
    
    function generateUUID() {
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function(c) {
            var r = Math.random() * 16 | 0, v = c === 'x' ? r : (r & 0x3 | 0x8);
            return v.toString(16);
        });
    }

    function initSession() {
        try {
            const saved = localStorage.getItem(CONFIG.storageKey);
            if (saved) {
                sessionData = JSON.parse(saved);
                if (!sessionData.sessionId) {
                    sessionData.sessionId = generateUUID();
                }
            } else {
                sessionData = {
                    sessionId: generateUUID(),
                    history: [],
                    isOpen: false
                };
            }
        } catch (e) {
            console.error('Failed to parse session data', e);
            sessionData = {
                sessionId: generateUUID(),
                history: [],
                isOpen: false
            };
        }
    }

    function saveSession() {
        localStorage.setItem(CONFIG.storageKey, JSON.stringify(sessionData));
    }

    function renderMarkdown(text) {
        if (!text) return '';
        let html = text
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            // Bold
            .replace(/\*\*(.*?)\*\*/g, '<strong>$1</strong>')
            // Italic
            .replace(/\*(.*?)\*/g, '<em>$1</em>')
            // Code block
            .replace(/```([\s\S]*?)```/g, '<pre><code>$1</code></pre>')
            // Inline code
            .replace(/`(.*?)`/g, '<code>$1</code>')
            // Lists
            .replace(/^\s*-\s+(.*)/gm, '<ul><li>$1</li></ul>')
            .replace(/<\/ul>\n<ul>/g, '\n')
            // Newlines
            .replace(/\n/g, '<br>');
        return html;
    }

    function formatTime(dateString) {
        const date = dateString ? new Date(dateString) : new Date();
        const hours = date.getHours().toString().padStart(2, '0');
        const minutes = date.getMinutes().toString().padStart(2, '0');
        return `${hours}:${minutes}`;
    }

    function buildMessageElement(message) {
        const isUser = message.role === 'user';
        const msgDiv = document.createElement('div');
        msgDiv.className = `ai-chat-msg ${isUser ? 'ai-chat-msg-user' : 'ai-chat-msg-bot'}`;
        
        let contentHtml = `<div class="ai-chat-msg-bubble">`;
        if (!isUser && message.content === null) {
            // Typing indicator
            contentHtml += `<div class="ai-chat-typing"><span></span><span></span><span></span></div>`;
        } else {
            contentHtml += `<div class="ai-chat-msg-text">${renderMarkdown(message.content)}</div>`;
        }
        contentHtml += `</div>
            <div class="ai-chat-msg-time">${formatTime(message.timestamp)}</div>`;
        
        msgDiv.innerHTML = contentHtml;

        // Quick actions
        if (message.actions && message.actions.length > 0) {
            const actionsDiv = document.createElement('div');
            actionsDiv.className = 'ai-chat-actions';
            message.actions.forEach(action => {
                const btn = document.createElement('button');
                btn.className = 'ai-chat-action-btn';
                btn.textContent = action.label;
                btn.onclick = () => handleQuickAction(action.value);
                actionsDiv.appendChild(btn);
            });
            msgDiv.appendChild(actionsDiv);
        }

        return msgDiv;
    }

    function scrollToBottom() {
        if (elements.messagesContainer) {
            elements.messagesContainer.scrollTop = elements.messagesContainer.scrollHeight;
        }
    }

    function addMessage(role, content, actions = []) {
        const message = {
            id: generateUUID(),
            role: role,
            content: content,
            timestamp: new Date().toISOString(),
            actions: actions
        };
        sessionData.history.push(message);
        saveSession();
        
        const msgEl = buildMessageElement(message);
        elements.messagesContainer.appendChild(msgEl);
        scrollToBottom();
        
        if (!sessionData.isOpen && role !== 'user') {
            updateUnreadBadge(1);
            playNotificationSound();
        }
        
        return message.id;
    }

    function updateMessageContent(id, newContent) {
        const message = sessionData.history.find(m => m.id === id);
        if (message) {
            message.content = newContent;
            saveSession();
            renderHistory();
        }
    }

    let unreadCount = 0;
    function updateUnreadBadge(increment) {
        if (increment === 0) {
            unreadCount = 0;
            elements.badge.style.display = 'none';
        } else {
            unreadCount += increment;
            elements.badge.textContent = unreadCount;
            elements.badge.style.display = 'block';
        }
    }

    function playNotificationSound() {
        try {
            const audio = new Audio('/media/chat-notify.mp3'); // Optional sound file
            audio.play().catch(e => { /* Ignore auto-play errors */ });
        } catch(e) {}
    }

    function renderHistory() {
        elements.messagesContainer.innerHTML = '';
        if (sessionData.history.length === 0) {
            addMessage('assistant', CONFIG.welcomeMessage, [
                { label: '🔍 Còn bao nhiêu phòng trống?', value: 'Còn bao nhiêu phòng trống?' },
                { label: '📋 Xem danh sách phòng', value: '/Room/Listing' },
                { label: '🏷️ Bảng giá các loại phòng', value: 'Bảng giá các loại phòng hiện tại' }
            ]);
        } else {
            sessionData.history.forEach(msg => {
                elements.messagesContainer.appendChild(buildMessageElement(msg));
            });
            scrollToBottom();
        }
    }

    function setTypingState(isTyping) {
        isWaitingForResponse = isTyping;
        elements.sendBtn.disabled = isTyping;
        elements.input.disabled = isTyping;
        
        if (isTyping) {
            const msgEl = buildMessageElement({ role: 'assistant', content: null, timestamp: new Date().toISOString() });
            msgEl.id = 'ai-chat-typing-indicator';
            elements.messagesContainer.appendChild(msgEl);
            scrollToBottom();
        } else {
            const indicator = document.getElementById('ai-chat-typing-indicator');
            if (indicator) {
                indicator.remove();
            }
            elements.input.focus();
        }
    }

    async function sendMessage(text) {
        if (!text.trim() || isWaitingForResponse) return;
        
        addMessage('user', text);
        elements.input.value = '';
        setTypingState(true);

        const requestBody = {
            sessionId: sessionData.sessionId,
            message: text,
            role: window.aiChatUserRole || 'guest',
            history: (sessionData.history || [])
                .filter(m => m && m.content)
                .slice(-15)
                .map(m => ({
                    role: m.role === 'assistant' ? 'assistant' : 'user',
                    content: m.content
                }))
        };

        try {
            // Check if SSE stream endpoint is active (for future use), fallback to normal POST for now
            const useStream = false; // Toggle to true if SSE is fully implemented backend

            if (useStream) {
                await handleStreamResponse(requestBody);
            } else {
                const response = await fetch(CONFIG.apiEndpoint, {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json'
                    },
                    credentials: 'include',
                    body: JSON.stringify(requestBody)
                });

                if (!response.ok) {
                    throw new Error('API Error');
                }

                const data = await response.json();
                setTypingState(false);

                if (data.sessionId && data.sessionId !== sessionData.sessionId) {
                    sessionData.sessionId = data.sessionId;
                    saveSession();
                }

                const reply = data.reply || data.message || '';
                const rawActions = data.quickActions || data.actions || [];
                const actions = rawActions.map(a => ({
                    label: a.label || a.Label || '',
                    value: a.action || a.Action || a.value || a.Value || ''
                }));
                addMessage('assistant', reply, actions);
            }
        } catch (error) {
            console.error('Chat error:', error);
            setTypingState(false);
            addMessage('assistant', 'Xin lỗi, đã có lỗi xảy ra khi kết nối. Vui lòng thử lại sau.', [
                { label: 'Thử lại', value: text }
            ]);
        }
    }

    async function handleStreamResponse(requestBody) {
        // Simple mock for SSE implementation
        const response = await fetch(CONFIG.streamEndpoint, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(requestBody)
        });
        
        setTypingState(false);
        const reader = response.body.getReader();
        const decoder = new TextDecoder('utf-8');
        
        const msgId = addMessage('assistant', '');
        let accumulatedContent = '';

        while (true) {
            const { value, done } = await reader.read();
            if (done) break;
            
            const chunk = decoder.decode(value, { stream: true });
            const lines = chunk.split('\n');
            
            for (let line of lines) {
                if (line.startsWith('data: ')) {
                    try {
                        const data = JSON.parse(line.substring(6));
                        if (data.content) {
                            accumulatedContent += data.content;
                            updateMessageContent(msgId, accumulatedContent);
                        }
                    } catch (e) { console.error('Error parsing SSE', e); }
                }
            }
        }
    }

    function handleQuickAction(value) {
        if (!value) return;
        if (value.startsWith('/') || value.startsWith('http://') || value.startsWith('https://')) {
            window.location.href = value;
        } else {
            sendMessage(value);
        }
    }

    function toggleChat() {
        sessionData.isOpen = !sessionData.isOpen;
        saveSession();
        
        if (sessionData.isOpen) {
            elements.window.classList.add('ai-chat-open');
            elements.window.classList.remove('ai-chat-closed');
            elements.input.focus();
            updateUnreadBadge(0);
        } else {
            elements.window.classList.add('ai-chat-closed');
            elements.window.classList.remove('ai-chat-open');
        }
    }

    function startNewConversation() {
        if (confirm('Bạn có chắc chắn muốn xóa lịch sử và bắt đầu cuộc trò chuyện mới?')) {
            sessionData.history = [];
            sessionData.sessionId = generateUUID();
            saveSession();
            renderHistory();
        }
    }

    function bindEvents() {
        elements.fab.addEventListener('click', toggleChat);
        elements.closeBtn.addEventListener('click', toggleChat);
        elements.minimizeBtn.addEventListener('click', toggleChat);
        
        elements.sendBtn.addEventListener('click', () => {
            sendMessage(elements.input.value);
        });

        elements.input.addEventListener('keypress', (e) => {
            if (e.key === 'Enter') {
                sendMessage(elements.input.value);
            }
        });

        elements.newConvBtn.addEventListener('click', startNewConversation);
    }

    function init() {
        elements = {
            fab: document.getElementById('ai-chat-fab'),
            badge: document.getElementById('ai-chat-badge'),
            window: document.getElementById('ai-chat-window'),
            closeBtn: document.getElementById('ai-chat-close'),
            minimizeBtn: document.getElementById('ai-chat-minimize'),
            messagesContainer: document.getElementById('ai-chat-messages'),
            input: document.getElementById('ai-chat-input'),
            sendBtn: document.getElementById('ai-chat-send'),
            newConvBtn: document.getElementById('ai-chat-new-conv')
        };

        if (!elements.fab) return; // Not present on page

        initSession();
        renderHistory();
        bindEvents();

        if (sessionData.isOpen) {
            elements.window.classList.add('ai-chat-open');
            elements.window.classList.remove('ai-chat-closed');
            scrollToBottom();
        }
    }

    // Auto-initialize when DOM is ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

})();
