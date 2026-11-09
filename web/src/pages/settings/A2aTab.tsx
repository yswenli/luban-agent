import { createEffect, createSignal, Show } from 'solid-js';

import { resetAgent, settingsApi } from '../../lib/settings';
import type { A2AClient, A2AServer } from '../../lib/types';
import { showToast } from '../../stores/app';
import { AreaField, CheckField, FormActions, SettingsPane, TextField } from './Shared';

const emptyServer = (): A2AServer => ({
  host: '127.0.0.1',
  port: 0,
  path: '/a2a',
  requireApiKey: true,
  apiKeys: [],
  agentName: '',
  agentDescription: '',
  agentVersion: '1.0.0',
});

const emptyClient = (): A2AClient => ({
  key: '',
  name: '',
  endpoint: '',
  apiKey: '',
  registerAsSubAgent: true,
  remoteAgentName: '',
  allowAll: false,
});

const SERVER_KEY = 'server';

/**
 * A2A 页：工作区文件（`.luban-agent/a2a/*.json`）。
 *
 * 与桌面端一致：`server.json` 承载服务端角色，`client.<name>.json` 每个承载一个远端客户端；
 * 全局作用域无条目，由上层给出提示。
 */
export function A2aTab(props: { scope: string }) {
  const [clientKeys, setClientKeys] = createSignal<string[]>([]);
  const [active, setActive] = createSignal<string>(SERVER_KEY);
  const [server, setServer] = createSignal<A2AServer>(emptyServer());
  const [client, setClient] = createSignal<A2AClient>(emptyClient());
  const [busy, setBusy] = createSignal(false);

  const items = () => [SERVER_KEY, ...clientKeys()];

  const reload = async (keep?: string | null) => {
    try {
      const [serverData, clients] = await Promise.all([
        settingsApi.getA2AServer(props.scope),
        settingsApi.listA2AClients(props.scope),
      ]);
      setServer(serverData ?? emptyServer());
      setClientKeys(clients.map((item) => item.key));
      const next = keep ?? active();
      await open(next, clients);
    } catch (error) {
      showToast((error as Error).message, 'error');
    }
  };

  const open = async (key: string, known?: { key: string }[]) => {
    if (key === SERVER_KEY) {
      try {
        const data = await settingsApi.getA2AServer(props.scope);
        setServer(data ?? emptyServer());
        setActive(SERVER_KEY);
      } catch (error) {
        showToast((error as Error).message, 'error');
      }
      return;
    }

    const keys = known ? known.map((item) => item.key) : clientKeys();
    if (!keys.includes(key)) {
      setActive(SERVER_KEY);
      return;
    }

    try {
      const data = await settingsApi.getA2AClient(props.scope, key);
      setClient({ ...emptyClient(), ...data });
      setActive(key);
    } catch (error) {
      showToast((error as Error).message, 'error');
    }
  };

  createEffect(() => {
    props.scope;
    setActive(SERVER_KEY);
    setServer(emptyServer());
    setClientKeys([]);
    void reload(SERVER_KEY);
  });

  const patchServer = <K extends keyof A2AServer>(key: K, value: A2AServer[K]) =>
    setServer((current) => ({ ...current, [key]: value }));

  const patchClient = <K extends keyof A2AClient>(key: K, value: A2AClient[K]) =>
    setClient((current) => ({ ...current, [key]: value }));

  const isServer = () => active() === SERVER_KEY;

  const save = async () => {
    setBusy(true);
    try {
      if (isServer()) {
        await settingsApi.saveA2AServer({ scope: props.scope, ...server() });
        showToast('A2A 服务端配置已保存', 'success');
      } else {
        if (!client().name?.trim() || !client().endpoint?.trim()) {
          showToast('客户端名称与远端地址不能为空', 'error');
          return;
        }

        const result = await settingsApi.saveA2AClient({
          scope: props.scope,
          oldKey: client().key || null,
          name: client().name,
          endpoint: client().endpoint,
          apiKey: client().apiKey,
          registerAsSubAgent: client().registerAsSubAgent,
          remoteAgentName: client().remoteAgentName,
          allowAll: client().allowAll,
        });
        await resetAgent();
        showToast('A2A 客户端配置已保存', 'success');
        setActive(result?.key ?? active());
        await reload(result?.key ?? active());
        return;
      }

      await reload(SERVER_KEY);
    } catch (error) {
      showToast((error as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  const remove = async () => {
    const key = active();
    if (!window.confirm(key === SERVER_KEY ? '删除服务端配置 server.json？' : `删除客户端配置「${key}」？`)) return;

    setBusy(true);
    try {
      await settingsApi.deleteA2A(props.scope, key);
      await resetAgent();
      showToast('已删除 A2A 配置', 'success');
      setActive(SERVER_KEY);
      await reload(SERVER_KEY);
    } catch (error) {
      showToast((error as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  return (
    <SettingsPane
      title="A2A 条目"
      items={items()}
      activeKey={active()}
      emptyText="当前工作区还没有 A2A 配置。"
      createLabel="新建客户端"
      busy={busy()}
      onSelect={(key) => void open(key)}
      onCreate={() => {
        setActive('');
        setClient(emptyClient());
      }}
    >
      <Show
        when={isServer()}
        fallback={
          <div class="field-grid">
            <TextField label="名称" value={client().name ?? ''} onInput={(v) => patchClient('name', v)} />
            <TextField
              label="远端地址"
              value={client().endpoint ?? ''}
              placeholder="如 http://127.0.0.1:5001/a2a"
              onInput={(v) => patchClient('endpoint', v)}
            />
            <TextField
              label="出站 API Key"
              type="password"
              value={client().apiKey ?? ''}
              hint="保存在本机工作区文件；留空表示不设置"
              onInput={(v) => patchClient('apiKey', v)}
            />
            <TextField
              label="远端 Agent 名"
              value={client().remoteAgentName ?? ''}
              onInput={(v) => patchClient('remoteAgentName', v)}
            />
            <CheckField
              label="注册为本地子代理"
              checked={client().registerAsSubAgent}
              onChange={(v) => patchClient('registerAsSubAgent', v)}
            />
            <CheckField label="声明 AllowAll" checked={client().allowAll} onChange={(v) => patchClient('allowAll', v)} />
          </div>
        }
      >
        <div class="field-grid">
          <TextField label="监听地址" value={server().host} onInput={(v) => patchServer('host', v)} />
          <TextField
            label="端口"
            type="number"
            value={String(server().port)}
            hint="0 表示随机端口"
            onInput={(v) => patchServer('port', Number.parseInt(v, 10) || 0)}
          />
          <TextField label="端点前缀" value={server().path} onInput={(v) => patchServer('path', v)} />
          <TextField label="对外名称" value={server().agentName ?? ''} onInput={(v) => patchServer('agentName', v)} />
          <TextField
            label="对外版本"
            value={server().agentVersion ?? ''}
            onInput={(v) => patchServer('agentVersion', v)}
          />
          <CheckField
            label="要求 API Key"
            checked={server().requireApiKey}
            onChange={(v) => patchServer('requireApiKey', v)}
          />
          <AreaField
            label="API Key 列表"
            value={server().apiKeys.join('\n')}
            rows={4}
            wide
            mono
            hint="一行一个；要求 API Key 时必填至少一个"
            onInput={(v) => patchServer('apiKeys', v.split(/\r?\n/).map((line) => line.trim()).filter(Boolean))}
          />
          <AreaField
            label="对外描述"
            value={server().agentDescription ?? ''}
            rows={3}
            wide
            onInput={(v) => patchServer('agentDescription', v)}
          />
        </div>
      </Show>
      <FormActions>
        <button class="btn primary" onClick={() => void save()} disabled={busy()}>
          保存
        </button>
        <button class="btn danger" onClick={() => void remove()} disabled={busy()}>
          删除
        </button>
      </FormActions>
    </SettingsPane>
  );
}