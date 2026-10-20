(function ($) {
    'use strict';
    var account = Number(document.body.dataset.accountId || 0), authKey = 'nexus-active-account';
    try { if (document.body.dataset.authKnown === 'true') localStorage.setItem(authKey, String(account)); } catch (_) { }
    if (!account) return;
    var channel = document.querySelector('[data-realtime-channel]'), query = {}, source, retry, timer;
    var stopped = false, paused = false, refreshing = false, syncing = false, syncAgain = false, sending = false, writeAllowed = true;
    var notificationVersion, conversationVersion, channelVersion, retryMs = 1000;
    var cursor = channel ? Number(channel.dataset.lastMessage || 0) : 0;
    var history = channel && channel.dataset.history === 'true';
    var notificationHistory = Number(new URL(location.href).searchParams.get('page') || 1) > 1;
    var fragmentJobs = {};
    if (channel && channel.dataset.conversationId) query.conversationId = Number(channel.dataset.conversationId);
    if (channel && channel.dataset.workspaceId) query.workspaceId = Number(channel.dataset.workspaceId);
    var suffix = $.param(query), bell = $('a.icon-button[href="/Notifications"],a.icon-button[href="/Notifications/Index"]').first();
    var indicator = $('<span class="realtime-indicator" aria-hidden="true">').appendTo(bell);
    function status(state, text) {
        $('[data-live-status]').attr('data-state', state).text(text);
        indicator.attr('data-state', state).attr('title', text);
    }
    function close() { if (source) source.close(); source = null; clearTimeout(retry); }
    function halt(message) {
        stopped = true; close(); clearInterval(timer);
        status('stopped', message); $('[data-live-send-status]').text(message);
        if (channel) { $(channel).find('.chat-thread').empty().text(message); $(channel).find('.chat-compose :input').prop('disabled', true); $(channel).find('textarea').val(''); }
        $('.notification-list').empty().text(message);
        $('[data-realtime-conversations]').empty().text(message);
    }
    function failure(xhr) {
        if ([400, 401, 403, 404].indexOf(xhr.status) >= 0) halt('Phiên hoặc quyền truy cập đã thay đổi. Tải lại trang để tiếp tục.');
        else status('reconnecting', 'Đang chờ kết nối. Nội dung đã nhập vẫn được giữ.');
    }
    function count(unread) {
        bell.attr('aria-label', 'Thông báo · ' + unread + ' chưa đọc');
        bell.find('.notification-count').remove();
        if (unread > 0) $('<span class="notification-count" aria-hidden="true">').text(unread > 99 ? '99+' : unread).appendTo(bell);
        $('[data-notification-toolbar] > .pill').text(unread + ' chưa đọc');
    }
    // One in-flight refresh per fragment; late responses cannot overwrite a newer refresh.
    function fragment(key, url, selectors) {
        if (stopped || paused) return;
        var job = fragmentJobs[key];
        if (job) { job.again = true; return; }
        job = fragmentJobs[key] = { again: false };
        $.ajax({ url: url, dataType: 'html', timeout: 15000 }).done(function (html) {
            if (stopped || paused) return;
            var doc = new DOMParser().parseFromString(html, 'text/html');
            if (Number(doc.body.dataset.accountId) !== account) { halt('Tài khoản đã thay đổi. Tải lại trang để tiếp tục.'); return; }
            selectors.forEach(function (selector) {
                var node = doc.querySelector(selector), current = document.querySelector(selector);
                if (node && current) { node.querySelectorAll('script').forEach(function (script) { script.remove(); }); current.replaceWith(document.importNode(node, true)); }
            });
        }).fail(function (xhr) { if (key === 'notifications') notificationVersion = null; else conversationVersion = null; failure(xhr); }).always(function () {
            delete fragmentJobs[key]; if (job.again && !stopped) fragment(key, url, selectors);
        });
    }
    function append(message) {
        if (!channel || channel.querySelector('[data-message-id="' + message.id + '"]')) return;
        var log = channel.querySelector('.chat-thread'), atBottom = log.scrollHeight - log.scrollTop - log.clientHeight < 80;
        var article = document.createElement('article'); article.className = 'chat-message' + (message.authorId === account ? ' is-mine' : ''); article.dataset.messageId = message.id;
        var meta = document.createElement('div'); meta.className = 'chat-meta';
        var author = document.createElement('strong'); author.textContent = message.author;
        var time = document.createElement('time'); time.dateTime = message.createdAtUtc;
        time.textContent = new Intl.DateTimeFormat('vi-VN', { dateStyle: 'short', timeStyle: 'short', timeZone: 'Asia/Ho_Chi_Minh' }).format(new Date(message.createdAtUtc)) + ' (UTC+7)';
        var body = document.createElement('p'); body.className = 'chat-body'; body.textContent = message.body;
        meta.append(author, time); article.append(meta, body); log.append(article);
        if (atBottom || message.authorId === account) log.scrollTop = log.scrollHeight;
        else $(channel).find('[data-live-jump]').prop('hidden', false);
        $(channel).find('.empty').remove();
        // Bound DOM growth. Older history remains available through server pagination.
        var rows = channel.querySelectorAll('[data-message-id]');
        if (rows.length > 200) { rows[0].remove(); $(channel).find('[data-live-latest]').prop('hidden', false).text('Mở lại 50 tin nhắn mới nhất'); }
    }
    function canSend(allowed) {
        writeAllowed = allowed;
        if (channel) $(channel).find('.chat-compose button[type="submit"]').prop('disabled', !allowed || sending || stopped);
        if (!allowed && channel) $('[data-live-send-status]').text('Kênh hiện chỉ đọc. Lịch sử và nội dung đang nhập được giữ lại.');
    }
    function syncMessages() {
        if (!channel || stopped || paused || history) return;
        if (syncing) { syncAgain = true; return; }
        syncing = true;
        $.ajax({ url: '/Realtime/Messages', data: $.extend({ after: cursor }, query), dataType: 'json', timeout: 15000 }).done(function (data) {
            if (stopped || paused) return;
            if (data.accountId !== account) { halt('Tài khoản đã thay đổi. Tải lại trang.'); return; }
            data.items.forEach(append); cursor = Math.max(cursor, data.nextCursor); canSend(data.canSend);
            if (data.hasMore) syncAgain = true;
        }).fail(failure).always(function () {
            syncing = false;
            if (syncAgain && !stopped) { syncAgain = false; setTimeout(syncMessages, 50); }
        });
    }
    function apply(data) {
        if (stopped || paused) return;
        if (data.accountId !== account) { halt('Tài khoản đã thay đổi. Tải lại trang.'); return; }
        count(data.unreadCount);
        if (data.notificationsVersion !== notificationVersion) {
            if ($('.notification-list').length) {
                if (!notificationHistory) fragment('notifications', location.pathname + location.search,
                    ['.notification-list', '[data-notification-paging]']);
                else if (notificationVersion) $('[data-notification-new]').prop('hidden', false);
            }
            notificationVersion = data.notificationsVersion;
        }
        if (data.conversationsVersion !== conversationVersion && $('[data-realtime-conversations]').length)
            fragment('conversations', '/Messages', ['[data-realtime-conversations]']);
        conversationVersion = data.conversationsVersion;
        if (channel && (data.channelVersion !== channelVersion || data.latestMessageId > cursor)) {
            if (history && data.latestMessageId > cursor) $(channel).find('[data-live-latest]').prop('hidden', false);
            else syncMessages();
            channelVersion = data.channelVersion;
        }
        canSend(data.canSend); retryMs = 1000;
    }
    // Periodic reconciliation also catches cookie changes, proxy buffering and missed reconnects.
    function refresh() {
        if (refreshing || stopped || paused || !navigator.onLine) return;
        refreshing = true;
        $.ajax({ url: '/Realtime/State', data: query, dataType: 'json', timeout: 15000 }).done(function (data) {
            apply(data);
            if (!source && !stopped) status('reconnecting', window.EventSource ? 'Đã đồng bộ. Đang kết nối trực tiếp…' : 'Đồng bộ định kỳ · Trình duyệt không hỗ trợ kết nối trực tiếp');
        }).fail(failure).always(function () { refreshing = false; });
    }
    function connect() {
        if (stopped || paused || !navigator.onLine || source || !window.EventSource) return;
        source = new EventSource('/Realtime/Stream' + (suffix ? '?' + suffix : ''));
        source.addEventListener('state', function (event) {
            try { apply(JSON.parse(event.data)); if (!stopped) status('connected', 'Đã kết nối trực tiếp · Tự cập nhật tin mới'); }
            catch (_) { close(); refresh(); }
        });
        source.addEventListener('access', function () { halt('Phiên hoặc quyền truy cập đã thay đổi. Tải lại trang để tiếp tục.'); });
        source.addEventListener('rotate', function () { close(); retry = setTimeout(connect, 250 + Math.random() * 500); });
        source.onerror = function () {
            close(); if (stopped || paused) return;
            status('reconnecting', 'Đang kết nối lại · Tin đã lưu sẽ được đồng bộ'); refresh();
            retry = setTimeout(connect, retryMs + Math.random() * 500); retryMs = Math.min(retryMs * 2, 30000);
        };
    }
    $(document).on('submit', '[data-realtime-channel] form.chat-compose', function (event) {
        if (stopped || sending || !writeAllowed || !this.checkValidity()) { if (stopped || sending || !writeAllowed) event.preventDefault(); return; }
        event.preventDefault();
        var form = $(this), input = form.find('textarea'), original = input.val();
        if (!navigator.onLine) { $('[data-live-send-status]').text('Đang mất mạng. Tin nhắn chưa gửi; nội dung được giữ để bạn gửi khi kết nối lại.'); return; }
        sending = true; form.find('button[type="submit"]').prop('disabled', true);
        $('[data-live-send-status]').text('Đang gửi…');
        $.ajax({ url: this.action, method: 'POST', data: form.serialize(), dataType: 'json', timeout: 20000 }).done(function (data) {
            if (stopped || data.sent !== true) return;
            if (input.val() === original) input.val('');
            if (history) $(channel).find('[data-live-latest]').prop('hidden', false);
            $('[data-live-send-status]').text('Đã gửi.'); syncMessages(); refresh();
        }).fail(function (xhr) {
            var message = xhr.status === 429 ? 'Bạn gửi quá nhanh. Giữ nội dung và thử lại sau một phút.' :
                xhr.status === 400 ? 'Biểu mẫu đã hết hiệu lực. Sao chép nội dung rồi tải lại trang.' :
                xhr.status === 403 || xhr.status === 409 ? 'Không thể gửi vì quyền hoặc trạng thái kênh đã thay đổi.' :
                xhr.status === 422 ? 'Nhập nội dung từ 1 đến 2000 ký tự.' :
                'Chưa xác định được tin đã gửi hay chưa. Nội dung được giữ; kiểm tra lịch sử sau khi kết nối lại trước khi gửi lại.';
            $('[data-live-send-status]').text(message); syncMessages(); refresh();
        }).always(function () { sending = false; canSend(writeAllowed); refresh(); });
    }).on('submit', 'form[data-ajax-mark-all]', function (event) {
        if (notificationHistory) return;
        event.preventDefault(); var form = $(this), button = form.find('button'); button.prop('disabled', true);
        $.ajax({ url: this.action, method: 'POST', data: form.serialize(), timeout: 15000 }).done(function () {
            notificationVersion = null; refresh(); $('#notification-feedback').text('Đã đánh dấu tất cả đã đọc.');
        }).fail(function () { $('#notification-feedback').text('Chưa cập nhật được. Tải lại trang rồi thử lại.'); }).always(function () { button.prop('disabled', false); });
    }).on('nexus:refresh', function () { refresh(); });
    $('[data-live-jump]').on('click', function () { var log = channel.querySelector('.chat-thread'); log.scrollTop = log.scrollHeight; this.hidden = true; });
    if (channel && !history) { var log = channel.querySelector('.chat-thread'); log.scrollTop = log.scrollHeight; }
    window.addEventListener('storage', function (event) { if (event.key === authKey && event.newValue !== String(account)) halt('Tài khoản đã đổi hoặc đăng xuất ở tab khác. Tải lại trang.'); });
    window.addEventListener('offline', function () { close(); status('reconnecting', 'Đang mất mạng · Nội dung đã nhập được giữ'); });
    window.addEventListener('online', function () { refresh(); connect(); syncMessages(); });
    document.addEventListener('visibilitychange', function () {
        paused = document.hidden; if (paused) close(); else { refresh(); connect(); syncMessages(); }
    });
    window.addEventListener('pagehide', function () { paused = true; close(); clearInterval(timer); });
    window.addEventListener('pageshow', function (event) { if (event.persisted) { paused = false; refresh(); connect(); timer = setInterval(refresh, 8000); } });
    paused = document.hidden; status('reconnecting', 'Đang kết nối và đồng bộ…');
    refresh(); connect(); timer = setInterval(refresh, 8000);
})(jQuery);
