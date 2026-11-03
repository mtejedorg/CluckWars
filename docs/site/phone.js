/* Phone helpers, shared by the project sites. No dependencies; everything degrades to the plain page.
   - Section chips: uses an existing on-page contents list ([data-section-nav] or ul.toc) or builds one
     from the page's h2 headings (<main data-build-section-nav>; a heading's data-short gives the chip
     a shorter label), marks the section in view and keeps the active chip visible in its own row.
   - Scroll hints: wide tables (.table-scroll, .tw) and a scrolling chip row get a fade on the side
     that still has content, and become keyboard-focusable regions while they scroll.
   - Swipe rows ([data-swipe]) get a "2 / 5" counter that follows the swipe. */
(function () {
    'use strict';
    var slug = function (t) { return t.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, ''); };
    var reduceMotion = function () { return !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches); };
    var throttle = function (fn) {
        var pending = false;
        return function () { if (!pending) { pending = true; window.requestAnimationFrame(function () { pending = false; fn(); }); } };
    };

    // Scroll hints, used by tables and by a scrolling chip row
    var hinted = [];
    var hint = function (box, label) {
        var update = function () {
            var max = box.scrollWidth - box.clientWidth;
            var scrolls = max > 2;
            box.classList.toggle('more-right', scrolls && box.scrollLeft < max - 2);
            box.classList.toggle('more-left', scrolls && box.scrollLeft > 2);
            if (label) {
                if (scrolls) { box.setAttribute('tabindex', '0'); box.setAttribute('role', 'region'); box.setAttribute('aria-label', label); }
                else if (box.getAttribute('data-hint-a11y') === '1') { box.removeAttribute('tabindex'); box.removeAttribute('role'); box.removeAttribute('aria-label'); }
                if (scrolls) box.setAttribute('data-hint-a11y', '1');
            }
        };
        box.addEventListener('scroll', throttle(update), { passive: true });
        hinted.push(update);
        update();
    };

    // 1. Section chips
    var nav = document.querySelector('[data-section-nav]') || document.querySelector('ul.toc');
    var main = document.querySelector('main[data-build-section-nav]');
    if (!nav && main) {
        var heads = [].slice.call(main.querySelectorAll('h2'));
        if (heads.length >= 3) {
            nav = document.createElement('nav');
            nav.className = 'section-chips';
            nav.setAttribute('aria-label', 'On this page');
            nav.setAttribute('data-section-nav', '');
            heads.forEach(function (h) {
                // Reuse the section's id when the heading opens a <section id>, so no id is duplicated
                var sec = h.closest('section[id]');
                var id = h.id || (sec && sec.querySelector('h2') === h ? sec.id : '');
                if (!id) {
                    id = slug(h.textContent);
                    if (document.getElementById(id)) id = id + '-h';
                    h.id = id;
                }
                var a = document.createElement('a');
                a.href = '#' + id;
                a.textContent = h.getAttribute('data-short') || h.textContent;
                nav.appendChild(a);
            });
            // A direct child of <main> (sticky must not be trapped in a short hero), after the h1 and its tagline
            var anchor = main.querySelector('h1');
            while (anchor && anchor.parentNode !== main) anchor = anchor.parentNode;
            if (anchor && anchor.tagName === 'H1') {
                var next = anchor.nextElementSibling;
                if (next && next.matches('p.tagline, p.lede')) anchor = next;
            }
            main.insertBefore(nav, anchor ? anchor.nextSibling : main.firstChild);
        }
    }
    if (nav) {
        var links = [].slice.call(nav.querySelectorAll('a[href^="#"]'));
        var byId = {}, current;
        links.forEach(function (a) { byId[a.getAttribute('href').slice(1)] = a; });
        var reveal = function (a) {
            if (nav.scrollWidth <= nav.clientWidth + 2) return;
            var left = a ? a.offsetLeft - nav.offsetLeft - 16 : 0;
            nav.scrollTo({ left: Math.max(0, left), behavior: reduceMotion() ? 'auto' : 'smooth' });
        };
        var setActive = function (id) {
            if (id === current) return;
            current = id;
            links.forEach(function (a) {
                var on = a === byId[id];
                a.classList.toggle('is-active', on);
                if (on) a.setAttribute('aria-current', 'location'); else a.removeAttribute('aria-current');
            });
            reveal(byId[id] || null);
        };
        // Active = the last target whose top is above 35% of the viewport; at the very bottom of the page,
        // the last target (short final sections never reach the line).
        var targets = Object.keys(byId).map(function (id) { return document.getElementById(id); }).filter(Boolean);
        var update = function () {
            var line = window.innerHeight * 0.35, id = null;
            targets.forEach(function (t) { if (t.getBoundingClientRect().top <= line) id = t.id; });
            var doc = document.documentElement;
            if (targets.length && window.scrollY > 0 && window.scrollY + window.innerHeight >= doc.scrollHeight - 2) id = targets[targets.length - 1].id;
            setActive(id);
        };
        window.addEventListener('scroll', throttle(update), { passive: true });
        // After a resize or rotation the row may have reflowed: recompute and re-reveal the active chip
        window.addEventListener('resize', throttle(function () { current = undefined; update(); }));
        update();
        hint(nav, null);
    }

    // 2. Wide tables
    [].slice.call(document.querySelectorAll('.table-scroll, .tw')).forEach(function (box) { hint(box, 'Table, scrolls sideways'); });

    // 3. Swipe rows: position counter
    [].slice.call(document.querySelectorAll('[data-swipe]')).forEach(function (row) {
        var counter = document.createElement('div');
        counter.className = 'swipe-count';
        counter.setAttribute('aria-hidden', 'true');
        row.parentNode.insertBefore(counter, row.nextSibling);
        var update = function () {
            var items = [].slice.call(row.children);
            var idx = 0;
            items.forEach(function (c, i) { if (c.offsetLeft - row.offsetLeft <= row.scrollLeft + c.offsetWidth / 2) idx = i; });
            if (row.scrollLeft + row.clientWidth >= row.scrollWidth - 2) idx = items.length - 1;
            counter.textContent = (idx + 1) + ' / ' + items.length;
            var scrolls = row.scrollWidth > row.clientWidth + 2;
            if (scrolls) { row.setAttribute('tabindex', '0'); row.setAttribute('role', 'region'); row.setAttribute('aria-label', 'Cards, scroll sideways'); }
            else { row.removeAttribute('tabindex'); row.removeAttribute('role'); row.removeAttribute('aria-label'); }
        };
        row.addEventListener('scroll', throttle(update), { passive: true });
        hinted.push(update);
        update();
    });

    // Resizes, and clicks that may change a table (filters), refresh every hint and counter
    var refreshAll = throttle(function () { hinted.forEach(function (f) { f(); }); });
    window.addEventListener('resize', refreshAll);
    document.addEventListener('click', refreshAll);
})();
