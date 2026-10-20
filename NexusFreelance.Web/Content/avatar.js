(function () {
    'use strict';
    document.addEventListener('error', function (event) {
        if (event.target.tagName === 'IMG' && event.target.parentElement && event.target.parentElement.classList.contains('person-avatar')) event.target.remove();
    }, true);
    document.querySelectorAll('[data-avatar-picker]').forEach(function (form) {
        var input = form.querySelector('input[type=file]'), trigger = form.querySelector('[data-avatar-select]');
        var review = form.querySelector('.avatar-review'), status = form.querySelector('.avatar-feedback');
        var avatar = trigger.querySelector('.person-avatar'), original = avatar.innerHTML, previewUrl;
        function clear() {
            if (previewUrl) URL.revokeObjectURL(previewUrl);
            previewUrl = null; input.value = ''; review.hidden = true; avatar.innerHTML = original; status.textContent = '';
        }
        trigger.addEventListener('click', function () { input.click(); });
        form.querySelector('[data-avatar-cancel]').addEventListener('click', clear);
        input.addEventListener('change', function () {
            var file = input.files && input.files[0];
            if (!file) return;
            if (!/\.(png|jpe?g)$/i.test(file.name) || !/^image\/(png|jpeg)$/.test(file.type) || file.size > 2 * 1024 * 1024) {
                clear(); status.textContent = 'Chọn ảnh PNG hoặc JPEG tối đa 2 MB.'; return;
            }
            if (previewUrl) URL.revokeObjectURL(previewUrl);
            previewUrl = URL.createObjectURL(file);
            var img = document.createElement('img'); img.alt = ''; img.width = 96; img.height = 96;
            img.onload = function () {
                if (img.naturalWidth > 4096 || img.naturalHeight > 4096) { clear(); status.textContent = 'Ảnh tối đa 4096 × 4096 pixel.'; return; }
                avatar.replaceChildren(img); review.hidden = false; status.textContent = 'Đã chọn ảnh. Bấm “Dùng ảnh này” để cập nhật.';
            };
            img.onerror = function () { clear(); status.textContent = 'Không đọc được ảnh. Hãy chọn tệp PNG hoặc JPEG khác.'; };
            img.src = previewUrl;
        });
        form.addEventListener('submit', function () { status.textContent = 'Đang lưu ảnh đại diện…'; });
    });
}());
