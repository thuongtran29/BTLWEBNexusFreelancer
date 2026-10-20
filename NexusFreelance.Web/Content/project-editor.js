(function () {
    'use strict';
    var area = document.getElementById('Description');
    if (!area || !window.EasyMDE || !window.DOMPurify) return;
    var editor = new EasyMDE({ element: area, forceSync: true, spellChecker: false, autoDownloadFontAwesome: false,
        autosave: { enabled: false }, minHeight: '180px', status: false,
        toolbar: false,
        renderingConfig: { sanitizerFunction: function (html) {
            return DOMPurify.sanitize(html, { ALLOWED_TAGS: ['p','br','strong','em','ul','ol','li','h1','h2','h3','h4','blockquote','code','pre'], ALLOWED_ATTR: [] });
        } }
    });
    editor.codemirror.setOption('mode', 'text/plain');
    editor.codemirror.getWrapperElement().parentElement.classList.add('project-description-plain');
    var help = document.createElement('p'); help.className = 'form-help'; help.id = 'editor-status';
    help.setAttribute('aria-live', 'off'); editor.gui.statusbar ? editor.gui.statusbar.after(help) : area.parentNode.insertBefore(help, area.nextSibling);
    area.required = false;
    var input = editor.codemirror.getInputField(); input.id = 'DescriptionEditor'; input.setAttribute('aria-label', 'Mô tả chi tiết dự án'); input.setAttribute('aria-required', 'true');
    var label = document.querySelector('label[for="Description"]'); if (label) label.htmlFor = input.id;
    input.setAttribute('aria-describedby', help.id);
    function update() { help.textContent = editor.value().length + '/4000 ký tự · Nhập văn bản thông thường, xuống dòng để tách ý.'; }
    editor.codemirror.on('change', update); update();
    editor.codemirror.setOption('extraKeys', { Tab: false, 'Shift-Tab': false });
    area.form.addEventListener('submit', function (event) {
        if (!editor.value().trim() || editor.value().length > 4000) {
            event.preventDefault(); event.stopImmediatePropagation();
            help.setAttribute('role', 'alert'); help.textContent = 'Nhập mô tả từ 1 đến 4000 ký tự trước khi lưu.'; editor.codemirror.focus();
        }
    }, true);
})();
