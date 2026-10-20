/* Wallet-only, local QR rendering. Encoder: vendored qrcode-generator 1.4.4 (MIT). */
(function () {
    'use strict';
    var target = document.querySelector('[data-wallet-qr]');
    if (!target) return;
    var message = document.querySelector('[data-wallet-qr-error]');
    try {
        var payload = target.getAttribute('data-wallet-payload');
        if (!/^000201010212[0-9A-Z]+$/.test(payload) || payload.length > 300) throw new Error('Invalid wallet QR payload');
        var qr = qrcode(0, 'M');
        qr.addData(payload, 'Byte');
        qr.make();
        var count = qr.getModuleCount();
        var ns = 'http://www.w3.org/2000/svg';
        var svg = document.createElementNS(ns, 'svg');
        svg.setAttribute('viewBox', '0 0 ' + (count + 8) + ' ' + (count + 8));
        svg.setAttribute('role', 'img');
        svg.setAttribute('aria-label', target.getAttribute('aria-label'));
        svg.setAttribute('shape-rendering', 'crispEdges');
        var background = document.createElementNS(ns, 'rect');
        background.setAttribute('width', '100%');
        background.setAttribute('height', '100%');
        background.setAttribute('fill', '#fff');
        svg.appendChild(background);
        var path = document.createElementNS(ns, 'path');
        var cells = [];
        for (var row = 0; row < count; row++) {
            for (var column = 0; column < count; column++) {
                if (qr.isDark(row, column)) cells.push('M' + (column + 4) + ',' + (row + 4) + 'h1v1h-1z');
            }
        }
        path.setAttribute('d', cells.join(''));
        path.setAttribute('fill', '#000');
        svg.appendChild(path);
        target.appendChild(svg);
        message.hidden = true;
    } catch (error) {
        // Keep exact text bank details available when scripts or rendering fail.
        target.hidden = true;
        message.hidden = false;
    }
}());
