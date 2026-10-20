(function () {
    'use strict';
    var toggle = document.getElementById('navigation-toggle');
    var sidebar = document.getElementById('app-sidebar');
    var account = document.querySelector('.account-menu');
    if (account) {
        document.addEventListener('click', function (event) {
            if (!account.contains(event.target) && !event.target.closest('dialog')) account.open = false;
        });
        account.addEventListener('keydown', function (event) {
            if (event.key === 'Escape' && account.open) {
                account.open = false; account.querySelector('summary').focus();
            }
        });
    }
    var current = location.pathname.replace(/\/+$/, '').toLowerCase() || '/';
    document.querySelectorAll('.top-navigation').forEach(function (nav) {
        var matches = Array.from(nav.querySelectorAll('a')).filter(function (link) {
            var path = link.pathname.replace(/\/+$/, '').toLowerCase() || '/';
            return path === current || (path !== '/' && current.indexOf(path + '/') === 0);
        });
        var longest = matches.reduce(function (length, link) { return Math.max(length, link.pathname.length); }, 0);
        matches.forEach(function (link) { if (link.pathname.length === longest) link.setAttribute('aria-current', 'page'); });
    });
    var controller = (document.body.dataset.navController || '').toLowerCase();
    var action = (document.body.dataset.navAction || '').toLowerCase();
    var role = document.body.dataset.navRole;
    var sections = {
        dashboard: 'overview', contracts: 'contracts', workspace: 'contracts', workspacefiles: 'contracts',
        payments: 'contracts', projectmemory: 'contracts', wallet: 'wallet', profile: 'profile', clientprofile: 'profile',
        proposals: 'proposals', groups: 'groups', grouprecruitment: 'groups', knowledge: 'knowledge', skillevidence: 'evidence', assessments: 'assessments',
        contribution: 'contribution', notifications: 'notifications', account: 'security', reports: 'reports', messages: 'messages',
        jobstudio: 'create', projects: action === 'create' ? 'create' : role === 'Freelancer' ? 'marketplace' : 'projects',
        skills: action === 'certifications' || action === 'submitcertification' ? 'certifications' : 'skills',
        home: action === 'howitworks' ? 'guide' : 'marketplace',
        groupbids: role === 'Client' ? 'projects' : 'proposals',
        negotiation: role === 'Client' ? 'projects' : 'proposals'
    };
    var adminSections = { index: 'overview', users: 'users', skills: 'skills', createskill: 'skills', editskill: 'skills',
        questions: 'questions', createquestion: 'questions', editquestion: 'questions', badges: 'badges',
        certifications: 'certifications', reports: 'reports', audit: 'audit' };
    var selected = controller === 'admin' ? 'admin-' + adminSections[action] : sections[controller];
    document.querySelectorAll('.sidebar-nav a[data-nav-key]').forEach(function (link) {
        if (link.dataset.navKey === selected) {
            link.setAttribute('aria-current', 'page');
            var group = link.closest('details[data-nav-section]');
            if (group) group.open = true;
        }
    });
    if (!toggle || !sidebar) return;
    var mobile = window.matchMedia('(max-width: 750px)');
    var scrim = document.querySelector('.navigation-scrim');
    var closer = sidebar.querySelector('.sidebar-close');
    var collapsed = false, open = false;
    try { collapsed = localStorage.getItem('nexus.navigation.collapsed') === 'true'; } catch (_) { /* Storage is optional. */ }
    document.body.classList.add('navigation-ready');
    function render() {
        var expanded = mobile.matches ? open : !collapsed;
        sidebar.querySelectorAll('details[data-nav-section]').forEach(function (group) {
            var rail = !mobile.matches && collapsed;
            if (rail) {
                if (!group.hasAttribute('data-rail-open')) group.dataset.previousOpen = String(group.open);
                group.setAttribute('data-rail-open', 'true'); group.open = true;
            } else if (group.hasAttribute('data-rail-open')) {
                group.open = group.dataset.previousOpen === 'true';
                group.removeAttribute('data-rail-open'); delete group.dataset.previousOpen;
            }
        });
        document.body.classList.toggle('navigation-collapsed', !mobile.matches && collapsed);
        document.body.classList.toggle('navigation-open', mobile.matches && open);
        toggle.setAttribute('aria-expanded', String(expanded));
        toggle.setAttribute('aria-label', mobile.matches ? (open ? 'Đóng điều hướng' : 'Mở điều hướng') : (collapsed ? 'Mở rộng điều hướng' : 'Thu gọn điều hướng'));
        toggle.title = toggle.getAttribute('aria-label');
        scrim.hidden = !(mobile.matches && open);
        sidebar.inert = mobile.matches && !open;
        var modal = mobile.matches && open;
        document.querySelector('.header').inert = modal;
        document.getElementById('main-content').inert = modal;
        document.querySelector('footer').inert = modal;
        document.querySelector('.skip-link').inert = modal;
        if (modal) { sidebar.setAttribute('role', 'dialog'); sidebar.setAttribute('aria-modal', 'true'); }
        else { sidebar.removeAttribute('role'); sidebar.removeAttribute('aria-modal'); }
    }
    function close() { open = false; render(); toggle.focus(); }
    toggle.addEventListener('click', function () {
        if (mobile.matches) {
            open = !open; render(); if (open) closer.focus();
        } else {
            collapsed = !collapsed;
            try { localStorage.setItem('nexus.navigation.collapsed', String(collapsed)); } catch (_) { /* Storage is optional. */ }
            render();
        }
    });
    closer.addEventListener('click', close);
    scrim.addEventListener('click', close);
    document.addEventListener('keydown', function (event) {
        if (!mobile.matches || !open) return;
        if (event.key === 'Escape') { event.preventDefault(); close(); }
        if (event.key !== 'Tab') return;
        var focusable = Array.from(sidebar.querySelectorAll('a[href],button:not([disabled]),summary')).filter(function (el) {
            var closed = el.closest('details:not([open])');
            return el.getClientRects().length && (!closed || closed.querySelector('summary') === el);
        });
        var first = focusable[0], last = focusable[focusable.length - 1];
        if (event.shiftKey && (document.activeElement === first || !sidebar.contains(document.activeElement))) { event.preventDefault(); last.focus(); }
        else if (!event.shiftKey && (document.activeElement === last || !sidebar.contains(document.activeElement))) { event.preventDefault(); first.focus(); }
    });
    mobile.addEventListener('change', function () {
        var focusInSidebar = sidebar.contains(document.activeElement);
        open = false; render();
        if (focusInSidebar && (mobile.matches || document.activeElement === closer)) toggle.focus();
    });
    render();
}());
