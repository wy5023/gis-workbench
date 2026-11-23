(function () {
    'use strict';

    const $ = (sel, root = document) => root.querySelector(sel);
    const $$ = (sel, root = document) => Array.from(root.querySelectorAll(sel));

    const WEEKDAYS = ['周一', '周二', '周三', '周四', '周五', '周六', '周日'];
    const PRIORITY = { 2: '高', 1: '中', 0: '低' };
    const COURSE_COLORS = ['#5B8DEF', '#8C7BF0', '#22B8A6', '#E0587A', '#F0913A', '#4FAE6F', '#3B82F6', '#A855F7'];
    const LINK_COLORS = ['#5B8DEF', '#22B8A6', '#8C7BF0', '#F0913A', '#E0587A', '#4FAE6F', '#3B82F6', '#A855F7'];

    const state = {
        todos: [],
        courses: [],
        links: [],
        filter: 'all',
        selectedDay: weekdayOf(new Date()),
        theme: 'dark'
    };

    /* ---------------- 工具 ---------------- */

    function esc(value) {
        return String(value ?? '').replace(/[&<>"']/g, (ch) => ({
            '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
        }[ch]));
    }

    function weekdayOf(date) {
        return ((date.getDay() + 6) % 7) + 1;
    }

    function dateKey(date) {
        const y = date.getFullYear();
        const m = String(date.getMonth() + 1).padStart(2, '0');
        const d = String(date.getDate()).padStart(2, '0');
        return `${y}-${m}-${d}`;
    }

    function parseDate(key) {
        const [y, m, d] = key.split('-').map(Number);
        return new Date(y, m - 1, d);
    }

    function dayDiff(a, b) {
        return Math.round((parseDate(a) - parseDate(b)) / 86400000);
    }

    function startOfWeek(date) {
        const copy = new Date(date);
        copy.setHours(0, 0, 0, 0);
        copy.setDate(copy.getDate() - (weekdayOf(copy) - 1));
        return copy;
    }

    function addDays(date, days) {
        const copy = new Date(date);
        copy.setDate(copy.getDate() + days);
        return copy;
    }

    function dueInfo(due) {
        if (!due) return null;
        const diff = dayDiff(due, dateKey(new Date()));
        const base = `${Number(due.slice(5, 7))}月${Number(due.slice(8, 10))}日`;
        if (diff === 0) return { label: '今天', cls: 'is-today' };
        if (diff === 1) return { label: '明天', cls: '' };
        if (diff === -1) return { label: '昨天', cls: 'is-overdue' };
        if (diff < 0) return { label: `${base} · 逾期`, cls: 'is-overdue' };
        return { label: base, cls: '' };
    }

    function hostOf(url) {
        try {
            return new URL(url).hostname.replace(/^www\./, '');
        } catch {
            return '';
        }
    }

    function colorFor(text, palette) {
        let hash = 0;
        for (let i = 0; i < text.length; i += 1) {
            hash = (hash * 31 + text.charCodeAt(i)) % 9973;
        }
        return palette[hash % palette.length];
    }

    function safeColor(value, fallback = '#5B8DEF') {
        return /^#[0-9a-fA-F]{6}$/.test(String(value || '')) ? value : fallback;
    }

    function toast(message, type = 'ok') {
        const wrap = $('#toast-wrap');
        const el = document.createElement('div');
        el.className = 'toast' + (type === 'error' ? ' error' : '');
        el.textContent = message;
        wrap.appendChild(el);
        setTimeout(() => {
            el.classList.add('out');
            setTimeout(() => el.remove(), 240);
        }, 2400);
    }

    /* ---------------- 主题 ---------------- */

    function initTheme() {
        const stored = localStorage.getItem('wb-theme');
        const prefersLight = window.matchMedia('(prefers-color-scheme: light)').matches;
        applyTheme(stored || (prefersLight ? 'light' : 'dark'));
    }

    function applyTheme(theme) {
        state.theme = theme;
        document.documentElement.dataset.theme = theme;
        const meta = document.querySelector('meta[name="theme-color"]');
        if (meta) meta.content = theme === 'dark' ? '#080b13' : '#f4f6fc';
        localStorage.setItem('wb-theme', theme);
    }

    /* ---------------- 时钟与问候 ---------------- */

    function startClock() {
        const tick = () => {
            const now = new Date();
            const dateEl = $('#clock-date');
            const timeEl = $('#clock-time');
            if (dateEl) dateEl.textContent = `${now.getMonth() + 1}月${now.getDate()}日 ${WEEKDAYS[weekdayOf(now) - 1]}`;
            if (timeEl) timeEl.textContent = now.toLocaleTimeString('zh-CN', { hour12: false });
        };
        tick();
        setInterval(tick, 1000);
    }

    function renderGreeting() {
        const now = new Date();
        const hour = now.getHours();
        let hello = '晚上好';
        if (hour < 5) hello = '夜深了';
        else if (hour < 9) hello = '早上好';
        else if (hour < 12) hello = '上午好';
        else if (hour < 14) hello = '中午好';
        else if (hour < 18) hello = '下午好';

        const active = state.todos.filter((t) => !t.done).length;
        const todayCourses = state.courses.filter((c) => c.weekday === weekdayOf(now)).length;

        let title;
        if (active > 0) title = `${hello}，还有 ${active} 件事等你完成`;
        else if (todayCourses > 0) title = `${hello}，今天有 ${todayCourses} 节课`;
        else title = `${hello}，今天可以轻松一点`;

        const dateEl = $('#greet-date');
        if (dateEl) {
            dateEl.textContent = `${now.getFullYear()}年${now.getMonth() + 1}月${now.getDate()}日 ${WEEKDAYS[weekdayOf(now) - 1]}`;
        }
        const greetEl = $('#greeting');
        if (greetEl) greetEl.textContent = title;
    }

    /* ---------------- 接口 ---------------- */

    async function api(path, options = {}) {
        const opts = { ...options, headers: { ...(options.headers || {}) } };
        if (opts.body) opts.headers['Content-Type'] = 'application/json';
        const res = await fetch('/api' + path, opts);
        if (res.status === 401) {
            showLogin(false);
            const error = new Error('登录已失效，请重新登录');
            error.name = 'UnauthorizedError';
            throw error;
        }
        const text = await res.text();
        let data = null;
        if (text) {
            try { data = JSON.parse(text); } catch { data = null; }
        }
        if (!res.ok) {
            throw new Error((data && data.message) || `请求失败（${res.status}）`);
        }
        return data;
    }

    /* ---------------- 登录 ---------------- */

    let loginHintLoaded = false;

    function showLogin(defaultPassword) {
        $('#app-view').hidden = true;
        $('#login-view').hidden = false;
        if (defaultPassword && !loginHintLoaded) {
            $('#login-hint').hidden = false;
            loginHintLoaded = true;
        }
        setTimeout(() => {
            const input = $('#login-password');
            if (input && !input.value) input.focus();
        }, 60);
    }

    function showApp() {
        $('#login-view').hidden = true;
        $('#app-view').hidden = false;
    }

    function bindLogin() {
        $('#login-form').addEventListener('submit', async (event) => {
            event.preventDefault();
            const button = $('#login-submit');
            const errorEl = $('#login-error');
            const input = $('#login-password');
            if (!input.value) return;

            button.classList.add('is-loading');
            button.textContent = '正在进入…';
            errorEl.hidden = true;

            try {
                const res = await fetch('/api/auth/login', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ password: input.value })
                });
                let data = null;
                try { data = await res.json(); } catch { data = null; }
                if (!res.ok) throw new Error((data && data.message) || '登录失败，请稍后重试');

                input.value = '';
                await enterApp();
            } catch (err) {
                errorEl.textContent = err.message;
                errorEl.hidden = false;
            } finally {
                button.classList.remove('is-loading');
                button.textContent = '进入工作台';
            }
        });

        $('#toggle-password').addEventListener('click', () => {
            const input = $('#login-password');
            input.type = input.type === 'password' ? 'text' : 'password';
            input.focus();
        });

        $('#logout-btn').addEventListener('click', async () => {
            try {
                await fetch('/api/auth/logout', { method: 'POST' });
            } catch { /* 忽略网络错误 */ }
            state.todos = [];
            state.courses = [];
            state.links = [];
            showLogin(false);
        });
    }

    async function enterApp() {
        showApp();
        await loadData();
    }

    async function loadData() {
        document.body.classList.add('is-loading');
        try {
            const [todos, courses, links] = await Promise.all([
                api('/todos'),
                api('/courses'),
                api('/links')
            ]);
            state.todos = todos || [];
            state.courses = courses || [];
            state.links = links || [];
            renderAll();
        } catch (err) {
            if (err.name !== 'UnauthorizedError') toast(err.message || '数据加载失败', 'error');
        } finally {
            document.body.classList.remove('is-loading');
        }
    }

    /* ---------------- 待办渲染 ---------------- */

    function filteredTodos() {
        const today = dateKey(new Date());
        switch (state.filter) {
            case 'active': return state.todos.filter((t) => !t.done);
            case 'today': return state.todos.filter((t) => !t.done && t.dueDate === today);
            case 'done': return state.todos.filter((t) => t.done);
            default: return state.todos;
        }
    }

    function todoHtml(todo) {
        const info = todo.done ? null : dueInfo(todo.dueDate);
        const pri = PRIORITY[todo.priority] || '中';
        const priClass = todo.priority === 2 ? 'p-high' : todo.priority === 1 ? 'p-mid' : 'p-low';
        return `
            <li class="todo-item ${todo.done ? 'is-done' : ''}" data-id="${todo.id}">
                <button class="todo-check" type="button" data-action="toggle" aria-label="${todo.done ? '标记为未完成' : '标记为完成'}">
                    <svg><use href="#i-check"/></svg>
                </button>
                <div class="todo-body">
                    <div class="todo-title-row">
                        <span class="todo-title">${esc(todo.title)}</span>
                        ${info ? `<span class="tag tag-due ${info.cls}"><svg><use href="#i-calendar"/></svg>${esc(info.label)}</span>` : ''}
                        <span class="tag tag-pri ${priClass}">${pri}</span>
                    </div>
                    ${todo.notes ? `<p class="todo-notes">${esc(todo.notes)}</p>` : ''}
                </div>
                <div class="todo-actions">
                    <button class="icon-btn sm" type="button" data-action="edit" aria-label="编辑待办">
                        <svg><use href="#i-edit"/></svg>
                    </button>
                    <button class="icon-btn sm danger" type="button" data-action="delete" aria-label="删除待办">
                        <svg><use href="#i-trash"/></svg>
                    </button>
                </div>
            </li>`;
    }

    function renderTodos() {
        const items = filteredTodos();
        const list = $('#todo-list');
        list.innerHTML = items.map(todoHtml).join('');

        const emptyEl = $('#todo-empty');
        emptyEl.hidden = items.length > 0;
        if (items.length === 0) {
            const title = $('.empty-title', emptyEl);
            const desc = $('.empty-desc', emptyEl);
            if (state.filter === 'done') {
                title.textContent = '还没有完成的待办';
                desc.textContent = '完成一项待办后会出现在这里。';
            } else if (state.filter === 'today') {
                title.textContent = '今天没有待办';
                desc.textContent = '享受一段从容的时间，或新建一条待办。';
            } else if (state.filter === 'active') {
                title.textContent = '所有待办都完成了';
                desc.textContent = '给自己一点奖励，然后开始新的计划。';
            } else {
                title.textContent = '这里空空如也';
                desc.textContent = '新建一条待办，让今天更有条理。';
            }
        }

        $('#todo-count').textContent = state.todos.length;
        $('#stat-todo').textContent = state.todos.filter((t) => !t.done).length;
    }

    function bindTodoEvents() {
        $('#todo-list').addEventListener('click', async (event) => {
            const button = event.target.closest('[data-action]');
            if (!button) return;
            const item = button.closest('.todo-item');
            const id = Number(item.dataset.id);
            const todo = state.todos.find((t) => t.id === id);
            if (!todo) return;

            const action = button.dataset.action;
            if (action === 'toggle') {
                const nextDone = !todo.done;
                todo.done = nextDone;
                renderTodos();
                renderGreeting();
                renderSchedule();
                try {
                    await api(`/todos/${id}/done`, {
                        method: 'PATCH',
                        body: JSON.stringify({ done: nextDone })
                    });
                    if (nextDone) toast('已完成一项待办');
                } catch (err) {
                    todo.done = !nextDone;
                    renderTodos();
                    renderGreeting();
                    renderSchedule();
                    toast(err.message, 'error');
                }
            } else if (action === 'edit') {
                openTodoDialog(todo);
            } else if (action === 'delete') {
                const ok = await confirmDelete(`确定删除「${todo.title}」吗？删除后无法恢复。`);
                if (!ok) return;
                try {
                    await api(`/todos/${id}`, { method: 'DELETE' });
                    state.todos = state.todos.filter((t) => t.id !== id);
                    renderAll();
                    toast('待办已删除');
                } catch (err) {
                    toast(err.message, 'error');
                }
            }
        });

        $('#todo-filters').addEventListener('click', (event) => {
            const button = event.target.closest('.seg-btn');
            if (!button) return;
            state.filter = button.dataset.filter;
            $$('.seg-btn', $('#todo-filters')).forEach((el) => el.classList.toggle('is-active', el === button));
            renderTodos();
        });

        $('#add-todo').addEventListener('click', () => openTodoDialog(null));
    }

    /* ---------------- 待办弹窗 ---------------- */

    let editingTodoId = null;

    function openTodoDialog(todo) {
        editingTodoId = todo ? todo.id : null;
        $('#todo-dialog-title').textContent = todo ? '编辑待办' : '新建待办';
        $('#todo-title').value = todo ? todo.title : '';
        $('#todo-notes').value = todo && todo.notes ? todo.notes : '';
        $('#todo-due').value = todo && todo.dueDate ? todo.dueDate : '';
        const priority = todo ? todo.priority : 1;
        $$('#priority-picker input').forEach((input) => {
            input.checked = Number(input.value) === priority;
        });
        $('#todo-dialog').showModal();
        setTimeout(() => $('#todo-title').focus(), 60);
    }

    function bindTodoDialog() {
        $('#todo-form').addEventListener('submit', async (event) => {
            event.preventDefault();
            const title = $('#todo-title').value.trim();
            if (!title) {
                toast('请填写待办标题', 'error');
                return;
            }
            const checked = $('#priority-picker input:checked');
            const payload = {
                title,
                notes: $('#todo-notes').value.trim() || null,
                priority: checked ? Number(checked.value) : 1,
                dueDate: $('#todo-due').value || null
            };

            try {
                if (editingTodoId) {
                    await api(`/todos/${editingTodoId}`, { method: 'PUT', body: JSON.stringify(payload) });
                    toast('待办已更新');
                } else {
                    await api('/todos', { method: 'POST', body: JSON.stringify(payload) });
                    toast('待办已创建');
                }
                $('#todo-dialog').close();
                await reloadTodos();
            } catch (err) {
                toast(err.message, 'error');
            }
        });
    }

    async function reloadTodos() {
        state.todos = (await api('/todos')) || [];
        renderTodos();
        renderGreeting();
        renderSchedule();
    }

    /* ---------------- 课程表渲染 ---------------- */

    function courseHtml(course) {
        const color = safeColor(course.color);
        return `
            <div class="course-block" style="--c:${color}" data-course-id="${course.id}" role="button" tabindex="0">
                <div class="course-time">${esc(course.startTime)} – ${esc(course.endTime)}</div>
                <div class="course-name">${esc(course.name)}</div>
                <div class="course-meta">
                    ${course.location ? `<span><svg><use href="#i-pin"/></svg>${esc(course.location)}</span>` : ''}
                    ${course.teacher ? `<span><svg><use href="#i-book"/></svg>${esc(course.teacher)}</span>` : ''}
                </div>
                ${course.weeks ? `<div class="course-weeks">${esc(course.weeks)}</div>` : ''}
            </div>`;
    }

    function renderSchedule() {
        const todayWeekday = weekdayOf(new Date());
        const monday = startOfWeek(new Date());

        const columns = [];
        for (let day = 1; day <= 7; day += 1) {
            const date = addDays(monday, day - 1);
            const courses = state.courses
                .filter((c) => c.weekday === day)
                .sort((a, b) => a.startTime.localeCompare(b.startTime));
            columns.push(`
                <div class="day-col ${day === todayWeekday ? 'is-today' : ''} ${day === state.selectedDay ? 'is-active' : ''}" data-day="${day}">
                    <div class="day-head">
                        <span class="day-name">${WEEKDAYS[day - 1]}</span>
                        <span class="day-date">${date.getMonth() + 1}/${date.getDate()}</span>
                    </div>
                    ${courses.map(courseHtml).join('')}
                    <button class="day-add" type="button" data-add-day="${day}" aria-label="在${WEEKDAYS[day - 1]}添加课程">
                        <svg><use href="#i-plus"/></svg>
                    </button>
                </div>`);
        }
        $('#schedule').innerHTML = columns.join('');

        $('#day-tabs').innerHTML = WEEKDAYS.map((name, index) => {
            const day = index + 1;
            const hasCourse = state.courses.some((c) => c.weekday === day);
            return `<button class="day-tab ${day === state.selectedDay ? 'is-active' : ''} ${day === todayWeekday ? 'is-today' : ''} ${hasCourse ? 'has-course' : ''}" type="button" data-day="${day}">${name}</button>`;
        }).join('');

        $('#course-count').textContent = state.courses.length;
        $('#stat-course').textContent = state.courses.filter((c) => c.weekday === todayWeekday).length;
    }

    function bindScheduleEvents() {
        $('#schedule').addEventListener('click', (event) => {
            const addButton = event.target.closest('[data-add-day]');
            if (addButton) {
                openCourseDialog(null, Number(addButton.dataset.addDay));
                return;
            }
            const block = event.target.closest('[data-course-id]');
            if (block) {
                const course = state.courses.find((c) => c.id === Number(block.dataset.courseId));
                if (course) openCourseDialog(course);
            }
        });

        $('#schedule').addEventListener('keydown', (event) => {
            if (event.key !== 'Enter' && event.key !== ' ') return;
            const block = event.target.closest('[data-course-id]');
            if (!block) return;
            event.preventDefault();
            const course = state.courses.find((c) => c.id === Number(block.dataset.courseId));
            if (course) openCourseDialog(course);
        });

        $('#day-tabs').addEventListener('click', (event) => {
            const tab = event.target.closest('[data-day]');
            if (!tab) return;
            state.selectedDay = Number(tab.dataset.day);
            renderSchedule();
        });

        $('#add-course').addEventListener('click', () => openCourseDialog(null, state.selectedDay || weekdayOf(new Date())));
    }

    /* ---------------- 课程弹窗 ---------------- */

    let editingCourseId = null;

    function buildColorPicker() {
        $('#color-picker').innerHTML = COURSE_COLORS.map((color, index) => `
            <label>
                <input type="radio" name="course-color" value="${color}" ${index === 0 ? 'checked' : ''} />
                <span class="swatch" style="--c:${color}"></span>
            </label>`).join('');
    }

    function openCourseDialog(course, weekday) {
        editingCourseId = course ? course.id : null;
        $('#course-dialog-title').textContent = course ? '编辑课程' : '添加课程';
        $('#course-name').value = course ? course.name : '';
        $('#course-teacher').value = course && course.teacher ? course.teacher : '';
        $('#course-location').value = course && course.location ? course.location : '';
        $('#course-weekday').value = String(course ? course.weekday : (weekday || weekdayOf(new Date())));
        $('#course-weeks').value = course && course.weeks ? course.weeks : '';
        $('#course-start').value = course ? course.startTime : '08:00';
        $('#course-end').value = course ? course.endTime : '09:40';

        const color = course ? safeColor(course.color) : COURSE_COLORS[Math.floor(Math.random() * 4)];
        const input = $(`#color-picker input[value="${color}"]`);
        if (input) input.checked = true;

        $('#course-dialog').showModal();
        setTimeout(() => $('#course-name').focus(), 60);
    }

    function bindCourseDialog() {
        $('#course-form').addEventListener('submit', async (event) => {
            event.preventDefault();
            const payload = {
                name: $('#course-name').value.trim(),
                teacher: $('#course-teacher').value.trim() || null,
                location: $('#course-location').value.trim() || null,
                weekday: Number($('#course-weekday').value),
                startTime: $('#course-start').value,
                endTime: $('#course-end').value,
                weeks: $('#course-weeks').value.trim() || null,
                color: ($('#color-picker input:checked') || {}).value || COURSE_COLORS[0]
            };
            if (!payload.name) {
                toast('请填写课程名称', 'error');
                return;
            }
            if (payload.startTime >= payload.endTime) {
                toast('结束时间需晚于开始时间', 'error');
                return;
            }

            try {
                if (editingCourseId) {
                    await api(`/courses/${editingCourseId}`, { method: 'PUT', body: JSON.stringify(payload) });
                    toast('课程已更新');
                } else {
                    await api('/courses', { method: 'POST', body: JSON.stringify(payload) });
                    toast('课程已添加');
                }
                $('#course-dialog').close();
                state.selectedDay = payload.weekday;
                state.courses = (await api('/courses')) || [];
                renderAll();
            } catch (err) {
                toast(err.message, 'error');
            }
        });
    }

    /* ---------------- 常用链接 ---------------- */

    function linkHtml(link) {
        const host = hostOf(link.url);
        const color = colorFor(link.title, LINK_COLORS);
        const initial = (link.title || '?').trim().charAt(0).toUpperCase();
        return `
            <div class="link-tile" data-link-id="${link.id}">
                <a class="link-main" href="${esc(link.url)}" target="_blank" rel="noopener noreferrer">
                    <span class="link-avatar" style="--tile:${color}">${esc(initial)}</span>
                    <span class="link-meta">
                        <span class="link-name">${esc(link.title)}</span>
                        <span class="link-host">${esc(host || '链接')}</span>
                    </span>
                </a>
                <span class="link-actions">
                    <button class="icon-btn sm" type="button" data-link-action="edit" aria-label="编辑链接">
                        <svg><use href="#i-edit"/></svg>
                    </button>
                    <button class="icon-btn sm danger" type="button" data-link-action="delete" aria-label="删除链接">
                        <svg><use href="#i-trash"/></svg>
                    </button>
                </span>
            </div>`;
    }

    function renderLinks() {
        $('#link-grid').innerHTML = state.links.map(linkHtml).join('') + `
            <button class="link-add" type="button" id="link-add-tile">
                <svg><use href="#i-plus"/></svg><span>添加链接</span>
            </button>`;
        $('#link-count').textContent = state.links.length;
        $('#stat-link').textContent = state.links.length;
    }

    function bindLinkEvents() {
        $('#link-grid').addEventListener('click', async (event) => {
            if (event.target.closest('#link-add-tile')) {
                openLinkDialog(null);
                return;
            }
            const actionButton = event.target.closest('[data-link-action]');
            if (!actionButton) return;
            const tile = actionButton.closest('[data-link-id]');
            const id = Number(tile.dataset.linkId);
            const link = state.links.find((l) => l.id === id);
            if (!link) return;

            if (actionButton.dataset.linkAction === 'edit') {
                openLinkDialog(link);
                return;
            }
            const ok = await confirmDelete(`确定删除「${link.title}」吗？`);
            if (!ok) return;
            try {
                await api(`/links/${id}`, { method: 'DELETE' });
                state.links = state.links.filter((l) => l.id !== id);
                renderLinks();
                toast('链接已删除');
            } catch (err) {
                toast(err.message, 'error');
            }
        });

        $('#add-link').addEventListener('click', () => openLinkDialog(null));
    }

    let editingLinkId = null;

    function openLinkDialog(link) {
        editingLinkId = link ? link.id : null;
        $('#link-dialog-title').textContent = link ? '编辑链接' : '添加链接';
        $('#link-title').value = link ? link.title : '';
        $('#link-url').value = link ? link.url : '';
        $('#link-category').value = link && link.category ? link.category : '';
        $('#link-dialog').showModal();
        setTimeout(() => $('#link-title').focus(), 60);
    }

    function bindLinkDialog() {
        $('#link-form').addEventListener('submit', async (event) => {
            event.preventDefault();
            const payload = {
                title: $('#link-title').value.trim(),
                url: $('#link-url').value.trim(),
                category: $('#link-category').value.trim() || null
            };
            if (!payload.title) {
                toast('请填写链接名称', 'error');
                return;
            }
            if (!payload.url) {
                toast('请填写链接地址', 'error');
                return;
            }

            try {
                if (editingLinkId) {
                    await api(`/links/${editingLinkId}`, { method: 'PUT', body: JSON.stringify(payload) });
                    toast('链接已更新');
                } else {
                    await api('/links', { method: 'POST', body: JSON.stringify(payload) });
                    toast('链接已添加');
                }
                $('#link-dialog').close();
                state.links = (await api('/links')) || [];
                renderLinks();
            } catch (err) {
                toast(err.message, 'error');
            }
        });
    }

    /* ---------------- 确认弹窗 ---------------- */

    function confirmDelete(message) {
        return new Promise((resolve) => {
            const dialog = $('#confirm-dialog');
            const okButton = $('#confirm-ok');
            $('#confirm-text').textContent = message;

            let settled = false;
            const finish = (result) => {
                if (settled) return;
                settled = true;
                okButton.removeEventListener('click', onOk);
                dialog.removeEventListener('close', onClose);
                resolve(result);
            };
            const onOk = () => {
                dialog.close();
                finish(true);
            };
            const onClose = () => finish(false);

            okButton.addEventListener('click', onOk);
            dialog.addEventListener('close', onClose);
            dialog.showModal();
        });
    }

    /* ---------------- 通用弹窗行为 ---------------- */

    function bindDialogs() {
        $$('dialog.modal').forEach((dialog) => {
            dialog.addEventListener('click', (event) => {
                if (event.target === dialog) dialog.close();
            });
            $$('[data-close]', dialog).forEach((button) => {
                button.addEventListener('click', () => dialog.close());
            });
        });
    }

    /* ---------------- 渲染入口 ---------------- */

    function renderAll() {
        renderGreeting();
        renderTodos();
        renderSchedule();
        renderLinks();
    }

    /* ---------------- 启动 ---------------- */

    async function boot() {
        initTheme();
        startClock();
        bindLogin();
        bindTodoEvents();
        bindTodoDialog();
        bindScheduleEvents();
        bindCourseDialog();
        bindLinkEvents();
        bindLinkDialog();
        bindDialogs();
        buildColorPicker();

        document.getElementById('theme-toggle').addEventListener('click', () => {
            applyTheme(state.theme === 'dark' ? 'light' : 'dark');
        });

        try {
            const res = await fetch('/api/auth/me');
            const me = await res.json();
            if (me.authenticated) {
                await enterApp();
            } else {
                showLogin(me.defaultPassword);
            }
        } catch {
            showLogin(false);
        }
    }

    document.addEventListener('DOMContentLoaded', boot);
})();
