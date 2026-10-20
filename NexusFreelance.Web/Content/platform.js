(function () {
    'use strict';
    var back = document.querySelector('[data-page-back]');
    if (back && document.referrer) {
        try {
            var previous = new URL(document.referrer), here = new URL(window.location.href);
            var sensitive = /^\/Account\/(Login|GoogleCallback|ResetPassword|VerifyEmail|ConfirmRegistration)/i;
            if (previous.origin === here.origin && previous.pathname + previous.search !== here.pathname + here.search && !sensitive.test(previous.pathname)) {
                back.href = previous.pathname + previous.search + previous.hash;
                back.title = 'Quay lại trang trước';
                var hint = back.querySelector('small'); if (hint) hint.textContent = 'Trang vừa xem';
            }
        } catch (_) { /* Keep the server-generated fallback for direct visits. */ }
    }
    document.querySelectorAll('[data-refund-target]').forEach(function (button) {
        button.addEventListener('click', function () {
            var input = document.getElementById(button.getAttribute('data-refund-target'));
            if (input) { input.value = button.getAttribute('data-refund-amount'); input.focus(); }
        });
    });
    document.querySelectorAll('form[data-ai-generate]').forEach(function (form) {
        var button = form.querySelector('button[type="submit"]'), status = form.querySelector('[data-ai-progress]');
        var text = button.textContent;
        form.addEventListener('submit', function () {
            form.setAttribute('aria-busy', 'true'); button.disabled = true; button.textContent = 'Đang soạn JD…';
            if (status) status.textContent = 'Đang chọn tri thức và soạn yêu cầu. Có thể mất tới 90 giây; vui lòng giữ trang này mở.';
        });
        window.addEventListener('pageshow', function () { button.disabled = false; button.textContent = text; form.removeAttribute('aria-busy'); });
    });
}());
