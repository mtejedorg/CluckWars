/* Phone helpers, shared by the project sites. No dependencies; everything degrades to the plain page.
   - Section chips: uses an existing on-page contents list ([data-section-nav]) or builds one from the
     page's h2 headings ([data-build-section-nav] on <main>), marks the section in view and keeps the
     active chip visible in its own scrolling row.
   - Wide tables: elements marked [data-scroll-hint] get a fade on the side that still has content.
   - Swipe rows: elements marked [data-swipe] get a "2 / 5" counter that follows the swipe. */
(function () {
    'use strict';
    var slug = function (t) { return t.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, ''); };

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
                if (!h.id) h.id = slug(h.textContent);
                var a = document.createElement('a');
                a.href = '#' + h.id;
                a.textContent = h.getAttribute('data-short') || h.textContent;
                nav.appendChild(a);
            });
            // Insert as a direct child of <main> (after the block holding the h1), so position: sticky
            // is not trapped inside a short hero wrapper.
            var anchor = main.querySelector('h1');
            while (anchor && anchor.parentNode !== main) anchor = anchor.parentNode;
            main.insertBefore(nav, anchor ? anchor.nextSibling : main.firstChild);
        }
    }
    if (nav) {
        var links = [].slice.call(nav.querySelectorAll('a[href^="#"]'));
        var byId = {}, current = null;
        links.forEach(function (a) { byId[a.getAttribute('href').slice(1)] = a; });
        var setActive = function (id) {
            if (id === current) return;
            current = id;
            links.forEach(function (a) {
                var on = a === byId[id];
                a.classList.toggle('is-active', on);
                if (on) a.setAttribute('aria-current', 'location'); else a.removeAttribute('aria-current');
                if (on && nav.scrollWidth > nav.clientWidth) {
                    var left = a.offsetLeft - nav.offsetLeft - 16;
                    nav.scrollTo({ left: left, behavior: 'smooth' });
                }
            });
        };
        // Active = the last target whose top is above 35% of the viewport. Computed on every scroll,
        // so a jump straight to the middle of the page is still right.
        var targets = Object.keys(byId).map(function (id) { return document.getElementById(id); }).filter(Boolean);
        var pending = false;
        var update = function () {
            pending = false;
            var line = window.innerHeight * 0.35, id = null;
            targets.forEach(function (t) { if (t.getBoundingClientRect().top <= line) id = t.id; });
            setActive(id);
        };
        window.addEventListener('scroll', function () { if (!pending) { pending = true; window.requestAnimationFrame(update); } }, { passive: true });
        window.addEventListener('resize', update);
        update();
    }

    // 2. Wide tables: fade on the side with hidden content
    [].slice.call(document.querySelectorAll('[data-scroll-hint], .table-scroll, .tw')).forEach(function (box) {
        var update = function () {
            var max = box.scrollWidth - box.clientWidth;
            box.classList.toggle('more-right', max > 2 && box.scrollLeft < max - 2);
            box.classList.toggle('more-left', max > 2 && box.scrollLeft > 2);
        };
        box.addEventListener('scroll', update, { passive: true });
        window.addEventListener('resize', update);
        update();
    });

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
        };
        row.addEventListener('scroll', function () { window.requestAnimationFrame(update); }, { passive: true });
        window.addEventListener('resize', update);
        update();
    });
})();
