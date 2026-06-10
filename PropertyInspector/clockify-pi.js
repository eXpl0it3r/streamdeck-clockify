// Clockify Property Inspector – kaskadierende Live-Dropdowns.
// Hängt sich über das EasyPI-Event 'websocketCreate' ein und ergänzt einen
// eigenen message-Listener (addEventListener), ohne sdtools.common.js zu ändern.

(function () {
    // fieldKey -> { name: hiddenNameId, manual: hiddenCheckboxId, request, children }
    var FIELDS = {
        workspace: { name: 'workspaceName', manual: 'workspaceManual', request: 'getWorkspaces', children: ['client', 'project'] },
        client:    { name: 'clientName',    manual: 'clientManual',    request: 'getClients',    children: ['project'] },
        project:   { name: 'projectName',   manual: 'projectManual',   request: 'getProjects',   children: ['task'] },
        task:      { name: 'taskName',      manual: 'taskManual',      request: 'getTasks',      children: [] }
    };

    function $(id) { return document.getElementById(id); }
    function selectEl(key) { return $(key + 'Select'); }
    function textEl(key) { return $(key + 'Text'); }
    function toggleEl(key) { return $(key + 'Toggle'); }
    function hiddenName(key) { return $(FIELDS[key].name); }
    function hiddenManual(key) { return $(FIELDS[key].manual); }

    function currentValue(key) {
        return FIELDS[key] ? (hiddenName(key).value || '') : '';
    }

    // Schreibt UI -> verstecktes Quell-Feld und persistiert über EasyPI.
    function commit(key, value, manual) {
        hiddenName(key).value = value || '';
        hiddenManual(key).checked = !!manual;
        if (typeof setSettings === 'function') {
            setSettings();
        }
    }

    function requestList(key) {
        var payload = {
            request: FIELDS[key].request,
            apiKey: ($('apiKey') ? $('apiKey').value : '') || '',
            serverUrl: ($('serverUrl') ? $('serverUrl').value : '') || '',
            workspaceName: currentValue('workspace'),
            clientName: currentValue('client'),
            projectName: currentValue('project')
        };
        if (typeof sendPayloadToPlugin === 'function') {
            sendPayloadToPlugin(payload);
        }
    }

    // Befüllt das Dropdown und stellt die gespeicherte Auswahl wieder her.
    // Ist der gespeicherte Wert nicht in der Liste, wird er als Option ergänzt,
    // damit die Auswahl nicht still verloren geht.
    function fillDropdown(key, items) {
        var sel = selectEl(key);
        var saved = hiddenName(key).value || '';
        sel.options.length = 0;

        var emptyOpt = document.createElement('option');
        emptyOpt.value = '';
        emptyOpt.text = '(keine Auswahl)';
        sel.appendChild(emptyOpt);

        var found = false;
        for (var i = 0; i < items.length; i++) {
            var opt = document.createElement('option');
            opt.value = items[i];
            opt.text = items[i];
            sel.appendChild(opt);
            if (items[i] === saved) { found = true; }
        }
        if (saved && !found) {
            var keep = document.createElement('option');
            keep.value = saved;
            keep.text = saved + ' (gespeichert)';
            sel.appendChild(keep);
        }
        sel.value = saved;
    }

    function onPluginMessage(evt) {
        var data;
        try { data = JSON.parse(evt.data); } catch (e) { return; }
        if (data.event !== 'sendToPropertyInspector' || !data.payload) { return; }
        var response = data.payload.response;
        var items = data.payload.items || [];
        var key = null;
        for (var k in FIELDS) {
            if (FIELDS[k].request === response) { key = k; break; }
        }
        if (key) { fillDropdown(key, items); }
    }

    function setManualMode(key, manual) {
        if (manual) {
            selectEl(key).classList.add('cf-hidden');
            textEl(key).classList.remove('cf-hidden');
        } else {
            textEl(key).classList.add('cf-hidden');
            selectEl(key).classList.remove('cf-hidden');
        }
    }

    function cascadeFrom(key) {
        // Bewusst KEIN Reset abhängiger Felder: bei Workspace-Wechsel nutzt
        // requestList('project') vorerst den bisherigen clientName. Für den
        // Patfor-Fall gewollt – nicht zu einem Reset "korrigieren".
        var children = FIELDS[key].children;
        for (var i = 0; i < children.length; i++) {
            requestList(children[i]);
        }
    }

    function wireField(key) {
        // Dropdown-Auswahl -> Quelle + Kaskade.
        selectEl(key).addEventListener('change', function () {
            commit(key, selectEl(key).value, false);
            cascadeFrom(key);
        });
        // Freitext -> Quelle (+ Kaskade, da abhängige Felder neu laden müssen).
        textEl(key).addEventListener('input', function () {
            commit(key, textEl(key).value, true);
        });
        textEl(key).addEventListener('change', function () {
            cascadeFrom(key);
        });
        // Umschalter Liste/Freitext.
        toggleEl(key).addEventListener('click', function () {
            var manual = !hiddenManual(key).checked;
            setManualMode(key, manual);
            var value = manual ? textEl(key).value : selectEl(key).value;
            commit(key, value, manual);
        });
    }

    // Liest die von EasyPI geladenen versteckten Felder und initialisiert die UI.
    function initFromSettings() {
        for (var key in FIELDS) {
            var manual = hiddenManual(key).checked;
            var value = hiddenName(key).value || '';
            setManualMode(key, manual);
            if (manual) {
                textEl(key).value = value;
            }
        }
        // Auto-Load der Wurzel; Kaskade folgt über die Antworten + gespeicherte Werte.
        var apiKey = $('apiKey') ? $('apiKey').value : '';
        if (apiKey) {
            requestList('workspace');
            requestList('client');
            requestList('project');
            requestList('task');
        }
    }

    function attach() {
        if (typeof websocket === 'undefined' || !websocket) { return; }
        websocket.addEventListener('message', onPluginMessage);
        for (var key in FIELDS) { wireField(key); }
        $('cfRefresh').addEventListener('click', function () {
            requestList('workspace');
            requestList('client');
            requestList('project');
            requestList('task');
        });
        // Initiales Laden erst, wenn der Socket OFFEN ist. Beim 'websocketCreate'
        // ist der Socket noch CONNECTING; ein setTimeout(0) liefe vor 'open' und
        // sendPayloadToPlugin (readyState !== 1) würde alle Requests verwerfen.
        // loadConfiguration der Lib hat die versteckten Felder zu diesem Zeitpunkt
        // bereits synchron gefüllt, daher ist die Auswahl-Wiederherstellung intakt.
        if (websocket.readyState === 1) {
            initFromSettings();
        } else {
            websocket.addEventListener('open', initFromSettings);
        }
    }

    document.addEventListener('websocketCreate', attach);
})();
