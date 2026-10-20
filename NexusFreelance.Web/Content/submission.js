(function ($) {
    'use strict';
    // Progressive enhancement: without JavaScript the original POST still works.
    $(document).on('submit', 'form[data-ajax-read]', function (event) {
        event.preventDefault();
        var form = $(this), row = form.closest('[data-notification-id]'), button = form.find('button');
        button.prop('disabled', true);
        $.ajax({ url: '/api/v1/notifications/' + row.attr('data-notification-id'), method: 'PATCH',
            data: { read: true }, dataType: 'json', timeout: 15000,
            headers: { 'X-CSRF-Token': form.find('input[name="__RequestVerificationToken"]').val() }
        }).done(function () {
            form.remove();
            row.removeClass('is-unread');
            row.find('[data-notification-status]').text('Đã đọc');
            row.find('form[action*="/Notifications/Open"] button').text('Mở nội dung');
            $('#notification-feedback').text('Đã đánh dấu thông báo.');
            $(document).trigger('nexus:refresh');
            $.getJSON('/api/v1/notifications', function (data) {
                var bell = $('a.icon-button[href="/Notifications"]');
                if (!bell.length) bell = $('a.icon-button[href="/Notifications/Index"]');
                bell.attr('aria-label', 'Thông báo · ' + data.unreadCount + ' chưa đọc');
                bell.find('.notification-count').remove();
                if (data.unreadCount) $('<span class="notification-count" aria-hidden="true">').text(data.unreadCount > 99 ? '99+' : data.unreadCount).appendTo(bell);
                $('.toolbar > .pill').text(data.unreadCount + ' chưa đọc');
            });
        }).fail(function (xhr) {
            button.prop('disabled', false);
            $('#notification-feedback').text(xhr.status === 400 || xhr.status === 401 || xhr.status === 403 ?
                'Phiên đã thay đổi. Tải lại trang rồi thử lại.' : 'Chưa cập nhật được. Vui lòng thử lại.');
        });
    });
})(jQuery);
