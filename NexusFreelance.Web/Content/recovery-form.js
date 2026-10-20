(function () {
    'use strict';
    document.querySelectorAll('form[data-recovery-form]').forEach(function (form) {
        var button = form.querySelector('button[type="submit"]');
        var status = form.querySelector('[data-recovery-status]');
        var email = form.querySelector('input[type="email"]');
        var label = button.textContent, pending = false;
        function restore() {
            pending = false; button.disabled = false; button.textContent = label;
            form.removeAttribute('aria-busy'); status.hidden = true; status.textContent = '';
        }
        form.addEventListener('invalid', function (event) {
            if (event.target !== email) return;
            var message = email.validity.valueMissing ? 'Vui lòng nhập email đã đăng ký.' : 'Vui lòng nhập địa chỉ email hợp lệ, ví dụ ten@gmail.com.';
            email.setCustomValidity(message); status.hidden = false; status.textContent = message;
        }, true);
        form.addEventListener('input', function (event) {
            if (event.target === email) { email.setCustomValidity(''); if (!pending) { status.hidden = true; status.textContent = ''; } }
        });
        form.addEventListener('submit', function (event) {
            if (event.defaultPrevented) return;
            if (pending) { event.preventDefault(); return; }
            pending = true; button.disabled = true; button.textContent = 'Đang gửi yêu cầu…';
            form.setAttribute('aria-busy', 'true'); status.hidden = false;
            status.textContent = 'Đang xử lý yêu cầu. Vui lòng đợi khoảng 10 giây và không bấm gửi lại.';
        });
        window.addEventListener('pageshow', restore);
    });
}());
