(function ($) {
    'use strict';
    if (!$('#marketplace').length || !window.history.pushState) return;
    var pending, sequence = 0;
    function load(url, push) {
        var target = new URL(url, window.location.href);
        if (target.origin !== window.location.origin || !/^\/(Home\/Index)?\/?$/i.test(target.pathname)) return;
        var request = ++sequence, area = $('#marketplace');
        if (pending) pending.abort();
        area.attr('aria-busy', 'true');
        area.find('[data-search-status]').text('Đang tìm dự án…');
        pending = $.ajax({ url: target.pathname + target.search, method: 'GET', dataType: 'html', timeout: 20000 });
        pending.done(function (html) {
            if (request !== sequence) return;
            var result = $('<div>').append($.parseHTML(html, document, false)).find('#marketplace');
            if (result.length !== 1) { failed(); return; }
            area.replaceWith(result);
            result.attr('aria-busy', 'false');
            result.find('[data-search-status]').text('Đã cập nhật kết quả. ' + result.find('.result-count').text());
            if (push) window.history.pushState({ marketplace: true }, '', target.pathname + target.search + '#marketplace');
            result.find('.marketplace-heading').attr('tabindex', '-1').trigger('focus');
        }).fail(function (_, status) { if (status !== 'abort' && request === sequence) failed(); });
        function failed() {
            area.attr('aria-busy', 'false');
            area.find('[data-search-status]').text('Chưa tải được kết quả. Kiểm tra bộ lọc hoặc kết nối rồi bấm Tìm dự án để thử lại.');
        }
    }
    $(document).on('submit', '#marketplace form.marketplace-search', function (event) {
        if (!this.checkValidity()) return;
        event.preventDefault();
        load(this.action + '?' + $(this).serialize(), true);
    }).on('click', '#marketplace .pagination a, #marketplace .filter-reset', function (event) {
        if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || event.button > 0) return;
        event.preventDefault(); load(this.href, true);
    });
    window.addEventListener('popstate', function () { load(window.location.href, false); });
    window.addEventListener('pagehide', function () { if (pending) pending.abort(); });
})(jQuery);
