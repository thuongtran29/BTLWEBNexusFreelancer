(function () {
    'use strict';
    document.querySelectorAll('input[type="password"]').forEach(function (input) {
        input.autocomplete = input.autocomplete || (window.location.pathname.toLowerCase().indexOf('login') >= 0 ? 'current-password' : 'new-password');
        var wrapper = document.createElement('div'); wrapper.className = 'password-field';
        input.parentNode.insertBefore(wrapper, input); wrapper.appendChild(input);
        var button = document.createElement('button'); button.type = 'button'; button.className = 'password-toggle';
        button.setAttribute('aria-controls', input.id); button.setAttribute('aria-pressed', 'false');
        var sprite = document.querySelector('.ui-icon use');
        var spriteUrl = sprite ? sprite.getAttribute('href').split('#')[0] : '/Content/icons.svg';
        var icon = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        icon.setAttribute('class', 'ui-icon'); icon.setAttribute('aria-hidden', 'true');
        var symbol = document.createElementNS('http://www.w3.org/2000/svg', 'use');
        symbol.setAttribute('href', spriteUrl + '#eye'); icon.appendChild(symbol);
        var label = document.createElement('span'); label.className = 'sr-only'; label.textContent = 'Hiện';
        button.title = 'Hiện mật khẩu'; button.setAttribute('aria-label', 'Hiện mật khẩu'); button.append(icon, label);
        button.addEventListener('click', function () {
            var show = input.type === 'password'; input.type = show ? 'text' : 'password';
            button.setAttribute('aria-pressed', String(show)); button.setAttribute('aria-label', show ? 'Ẩn mật khẩu' : 'Hiện mật khẩu');
            button.title = show ? 'Ẩn mật khẩu' : 'Hiện mật khẩu';
            symbol.setAttribute('href', spriteUrl + (show ? '#eye-off' : '#eye')); label.textContent = show ? 'Ẩn' : 'Hiện';
        }); wrapper.appendChild(button);
    });
    var confirmation = document.createElement('dialog'); confirmation.className = 'confirmation-dialog';
    confirmation.setAttribute('aria-labelledby', 'confirmation-title'); confirmation.setAttribute('aria-describedby', 'confirmation-message');
    var title = document.createElement('h2'); title.id = 'confirmation-title'; title.textContent = 'Xác nhận thao tác';
    var message = document.createElement('p'); message.id = 'confirmation-message';
    var cancel = document.createElement('button'); cancel.type = 'button'; cancel.className = 'btn btn-outline'; cancel.textContent = 'Ở lại / Hủy';
    var accept = document.createElement('button'); accept.type = 'button'; accept.className = 'btn btn-primary'; accept.textContent = 'Xác nhận';
    confirmation.append(title, message, cancel, accept); document.body.appendChild(confirmation);
    var pendingForm, pendingSubmitter;
    function clearPending() { var button = pendingSubmitter; pendingForm = null; pendingSubmitter = null; if (button) button.focus(); }
    cancel.addEventListener('click', function () { confirmation.close(); clearPending(); });
    confirmation.addEventListener('cancel', clearPending);
    accept.addEventListener('click', function () {
        var form = pendingForm, submitter = pendingSubmitter; confirmation.close(); pendingForm = null; pendingSubmitter = null;
        if (!form) return; form.dataset.confirmedOnce = 'true'; form.requestSubmit(submitter || undefined);
    });
    document.querySelectorAll('form[data-confirm]').forEach(function (form) {
        form.addEventListener('submit', function (event) {
            if (form.dataset.confirmedOnce === 'true') { delete form.dataset.confirmedOnce; return; }
            if (typeof confirmation.showModal !== 'function') { if (!window.confirm(form.getAttribute('data-confirm'))) event.preventDefault(); return; }
            event.preventDefault(); pendingForm = form; pendingSubmitter = event.submitter;
            message.textContent = form.getAttribute('data-confirm'); confirmation.showModal(); cancel.focus();
        });
    });
    var role = document.getElementById('Role'), googleButton = document.getElementById('GoogleLoginButton');
    if (role && googleButton) {
        function syncRole() { var ready=googleButton.dataset.googleReady==='true'; googleButton.disabled = !ready || role.value === 'Admin'; googleButton.textContent = role.value === 'Admin' ? 'Quản trị dùng email và mật khẩu' : !ready ? 'Google chưa sẵn sàng' : 'Tiếp tục bằng Google · ' + role.value; }
        role.addEventListener('change', syncRole); syncRole();
    }
}());
