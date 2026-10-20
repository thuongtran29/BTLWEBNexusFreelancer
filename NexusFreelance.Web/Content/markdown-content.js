(function () {
    'use strict';
    if (!window.marked || !window.DOMPurify) return;
    document.querySelectorAll('[data-markdown-content]').forEach(function (element) {
        var source = element.textContent;
        // Never allow inline HTML, links, images or scriptable attributes from project text.
        element.innerHTML = DOMPurify.sanitize(marked.parse(source, { breaks: true }), {
            ALLOWED_TAGS: ['p','br','strong','em','ul','ol','li','h1','h2','h3','h4','blockquote','code','pre'], ALLOWED_ATTR: []
        });
    });
})();
