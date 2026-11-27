import { createSignal, For, onMount, Show } from 'solid-js';

import { Modal } from './Dialog';
import { Icon } from './Icons';
import { t } from '../i18n';
import { settingsApi } from '../lib/settings';
import {
  browseWorkspace,
  getGitCredentialStatus,
  verifyGitRepository,
  type GitVerifyResult,
  type WorkspaceBrowse,
} from '../lib/workspaceGit';
import type { CreateWorkspaceInput, CreateWorkspaceResult } from '../stores/app';

/** 从仓库地址末段推断目录名（与服务端 `FolderFromGitUrl` 同口径）。 */
function repoFolder(url: string): string {
  const text = url.trim().replace(/[\\/]+$/, '');
  const index = Math.max(text.lastIndexOf('/'), text.lastIndexOf('\\'), text.lastIndexOf(':'));
  const name = index >= 0 ? text.slice(index + 1) : text;
  return (name.endsWith('.git') ? name.slice(0, -4) : name).trim();
}

/** 拼接根目录与相对子目录（只用于预览，实际路径由服务端归一化）。 */
function joinPath(root: string, sub: string): string {
  if (!root) return sub;
  if (!sub) return root;
  const separator = root.includes('\\') ? '\\' : '/';
  return `${root.replace(/[\\/]+$/, '')}${separator}${sub.replace(/^[\\/]+/, '')}`;
}

/**
 * 新建项目向导（三步）：来源 → 位置与凭据 → 确认。
 *
 * 位置固定为「管理员白名单根目录 + 相对子目录」，普通用户不能改根目录；
 * git 来源在下一步前先用 `git ls-remote` 预检地址与凭据，避免克隆到一半才报认证失败。
 */
export function NewProjectWizard(props: {
  onClose: () => void;
  onCreate: (input: CreateWorkspaceInput) => Promise<CreateWorkspaceResult>;
}) {
  const [step, setStep] = createSignal(1);
  const [source, setSource] = createSignal<'directory' | 'git'>('directory');
  const [roots, setRoots] = createSignal<string[]>([]);
  const [root, setRoot] = createSignal('');
  const [subPath, setSubPath] = createSignal('');
  const [name, setName] = createSignal('');
  const [gitUrl, setGitUrl] = createSignal('');
  const [gitUser, setGitUser] = createSignal('');
  const [gitSecret, setGitSecret] = createSignal('');
  const [saveCredential, setSaveCredential] = createSignal(true);
  const [savedCredentialUser, setSavedCredentialUser] = createSignal<string | null>(null);

  const [browse, setBrowse] = createSignal<WorkspaceBrowse | null>(null);
  const [browseOpen, setBrowseOpen] = createSignal(false);
  const [browseBusy, setBrowseBusy] = createSignal(false);

  const [verifying, setVerifying] = createSignal(false);
  const [verifiedKey, setVerifiedKey] = createSignal<string | null>(null);
  const [gitResult, setGitResult] = createSignal<GitVerifyResult | null>(null);

  const [saving, setSaving] = createSignal(false);
  const [error, setError] = createSignal<string | null>(null);
  const [conflict, setConflict] = createSignal<{ message: string; kind: 'reuse' | 'share' } | null>(null);

  /** 本次输入对应的校验指纹：URL 或凭据一变就要重新验证。 */
  const attemptKey = () => `${gitUrl().trim()}\n${gitUser().trim()}\n${gitSecret()}`;
  const verified = () => verifiedKey() !== null && verifiedKey() === attemptKey();

  /** 实际落点：git 留空时与服务端一致，取仓库名作为子目录。 */
  const effectiveSubPath = () =>
    subPath().trim() || (source() === 'git' ? repoFolder(gitUrl()) : '');

  const targetPath = () => joinPath(root(), effectiveSubPath());

  const canGoNext = () => {
    if (saving() || verifying() || browseBusy()) return false;
    if (step() === 1) return true;
    if (step() === 2) {
      // 目录来源必须选一个子目录；git 来源留空时按仓库名落一层
      return root().length > 0 && (source() === 'git' || subPath().trim().length > 0);
    }
    return false;
  };

  const loadRoots = async () => {
    try {
      const list = await settingsApi.listWorkspaceRoots();
      setRoots(list);
      if (list.length > 0 && !root()) {
        setRoot(list[0]);
        void openBrowse('');
      }
    } catch (err) {
      setError((err as Error).message);
    }
  };

  onMount(() => void loadRoots());

  const openBrowse = async (path: string) => {
    const currentRoot = root();
    if (!currentRoot) {
      setError(t('wizard.noRoots'));
      return;
    }

    setBrowseOpen(true);
    setBrowseBusy(true);
    setError(null);
    try {
      setBrowse(await browseWorkspace(currentRoot, path));
    } catch (err) {
      setError((err as Error).message);
      setBrowse(null);
    } finally {
      setBrowseBusy(false);
    }
  };

  const changeRoot = (value: string) => {
    setRoot(value);
    setSubPath('');
    setBrowse(null);
    setBrowseOpen(false);
    setError(null);
    if (value) void openBrowse('');
  };

  const pickDirectory = (path: string) => {
    if (!path) {
      // 根目录本身不能当项目目录（目录来源）；git 来源留空即落到仓库名子目录
      if (source() === 'directory') setError(t('wizard.subPathRequired'));
      setBrowseOpen(false);
      return;
    }

    setSubPath(path);
    setBrowseOpen(false);
    setError(null);
  };

  const changeSource = (next: 'directory' | 'git') => {
    if (saving() || source() === next) return;
    setSource(next);
    setError(null);
    setConflict(null);
    setGitResult(null);
    setVerifiedKey(null);
  };

  const resetVerification = () => {
    setVerifiedKey(null);
    setGitResult(null);
    setConflict(null);
    setError(null);
  };

  /** 预检仓库；返回 false 时停留在当前步骤并显示原因。 */
  const verify = async (): Promise<boolean> => {
    const url = gitUrl().trim();
    if (!url) {
      setError(t('sidebar.repoUrl'));
      return false;
    }

    const userName = gitUser().trim();
    const secret = gitSecret();
    if ((userName.length > 0) !== (secret.length > 0)) {
      setError(t('wizard.credentialRequired'));
      return false;
    }

    setVerifying(true);
    setError(null);
    setGitResult(null);
    try {
      const result = await verifyGitRepository(
        url,
        userName ? { userName, secret } : undefined,
      );
      setGitResult(result);
      if (result.ok) {
        setVerifiedKey(attemptKey());
        return true;
      }

      setVerifiedKey(null);
      setError(
        result.reason === 'git-ssh-unsupported'
          ? t('wizard.gitSshUnsupported')
          : result.message || t('wizard.gitVerifyFailed'),
      );
      return false;
    } catch (err) {
      setVerifiedKey(null);
      setError((err as Error).message);
      return false;
    } finally {
      setVerifying(false);
    }
  };

  const checkSavedCredential = async () => {
    if (source() !== 'git') return;
    const url = gitUrl().trim();
    if (!url) {
      setSavedCredentialUser(null);
      return;
    }

    try {
      const status = await getGitCredentialStatus(url);
      setSavedCredentialUser(status.saved ? status.userName ?? '' : null);
    } catch {
      setSavedCredentialUser(null);
    }
  };

  const next = async () => {
    setError(null);
    setConflict(null);

    if (step() === 1) {
      setStep(2);
      if (source() === 'git') void checkSavedCredential();
      else void openBrowse('');
      return;
    }

    if (step() !== 2) return;
    if (!root()) {
      setError(t('wizard.noRoots'));
      return;
    }

    if (source() === 'directory' && subPath().trim().length === 0) {
      setError(t('wizard.subPathRequired'));
      return;
    }

    if (source() === 'git' && !verified() && !(await verify())) {
      return;
    }

    setStep(3);
  };

  const back = () => {
    setError(null);
    setConflict(null);
    setStep((value) => Math.max(1, value - 1));
  };

  const submit = async (reuseExisting = false, shareExisting = false) => {
    if (saving() || step() !== 3) return;

    const userName = gitUser().trim();
    const secret = gitSecret();
    if (source() === 'git' && (userName.length > 0) !== (secret.length > 0)) {
      setConflict(null);
      setStep(2);
      setError(t('wizard.credentialRequired'));
      return;
    }

    setSaving(true);
    setError(null);
    try {
      const result = await props.onCreate({
        source: source(),
        rootPath: root(),
        subPath: effectiveSubPath(),
        gitUrl: source() === 'git' ? gitUrl().trim() : undefined,
        name: name().trim() || undefined,
        gitCredential:
          source() === 'git' && userName
            ? { userName, secret }
            : null,
        saveCredential: source() === 'git' && saveCredential() && userName.length > 0 && secret.length > 0,
        reuseExisting,
        shareExisting,
      });

      if (result.ok) {
        props.onClose();
        return;
      }

      if (result.code === 409 && result.reason === 'in-use') {
        setConflict({ message: result.error ?? t('sidebar.targetExists'), kind: 'share' });
      } else if (result.code === 409 && result.reason === 'exists') {
        setConflict({ message: result.error ?? t('sidebar.targetExists'), kind: 'reuse' });
      } else if (result.code === 409 && result.reason === 'target-not-empty') {
        setConflict(null);
        setVerifiedKey(null);
        setStep(2);
        setError(result.error ?? t('wizard.targetNotEmpty'));
      } else if (result.reason === 'target-file') {
        setConflict(null);
        setVerifiedKey(null);
        setStep(2);
        setError(result.error ?? t('sidebar.targetExists'));
      } else if (result.reason === 'root-missing') {
        setConflict(null);
        setVerifiedKey(null);
        setStep(2);
        setError(result.error ?? t('wizard.noRoots'));
        void loadRoots();
      } else if (result.reason?.startsWith('git-')) {
        // 地址 / 网络 / 凭据问题都要回到上一步改仓库地址或凭据，重新验证
        setConflict(null);
        setVerifiedKey(null);
        setStep(2);
        setError(result.error ?? t('wizard.gitVerifyFailed'));
      } else {
        setError(result.error ?? t('sidebar.createFailed'));
      }
    } finally {
      setSaving(false);
    }
  };

  const footer = () => (
    <>
      <button class="btn ghost" disabled={saving()} onClick={props.onClose}>
        {t('common.cancel')}
      </button>
      <div class="spacer" />
      <Show when={step() > 1}>
        <button class="btn ghost" disabled={saving()} onClick={back}>
          {t('wizard.back')}
        </button>
      </Show>
      <Show
        when={step() === 3}
        fallback={
          <button class="btn primary" disabled={!canGoNext()} onClick={() => void next()}>
            {verifying() ? t('wizard.verifying') : t('wizard.next')}
          </button>
        }
      >
        <Show
          when={conflict()}
          fallback={
            <button class="btn primary" disabled={saving()} onClick={() => void submit()}>
              {saving() ? t('common.creating') : t('common.create')}
            </button>
          }
        >
          <button class="btn primary" disabled={saving()} onClick={() => void submit(true, conflict()!.kind === 'share')}>
            {conflict()!.kind === 'share' ? t('sidebar.reuseWorkspace') : t('sidebar.reuseDirectory')}
          </button>
        </Show>
      </Show>
    </>
  );

  return (
    <Modal
      title={t('sidebar.newProject')}
      subtitle={t('sidebar.newProjectHint')}
      busy={saving()}
      onClose={props.onClose}
      footer={footer()}
    >
      <div class="wizard-steps">
        <For each={[t('wizard.stepSource'), t('wizard.stepLocation'), t('wizard.stepConfirm')]}>
          {(label, index) => (
            <div class="wizard-step" classList={{ active: index() + 1 === step(), done: index() + 1 < step() }}>
              <span class="wizard-step-index">{index() + 1}</span>
              <span>{label}</span>
            </div>
          )}
        </For>
      </div>

      <div class="form-stack">
        <Show when={step() === 1}>
          <div class="source-picker">
            <button
              type="button"
              class="source-option"
              classList={{ active: source() === 'directory' }}
              disabled={saving()}
              onClick={() => changeSource('directory')}
            >
              <Icon name="folder" size={18} />
              <span class="source-option-title">{t('sidebar.sourceDirectory')}</span>
              <span class="source-option-hint">{t('wizard.sourceDirectoryHint')}</span>
            </button>
            <button
              type="button"
              class="source-option"
              classList={{ active: source() === 'git' }}
              disabled={saving()}
              onClick={() => changeSource('git')}
            >
              <Icon name="git" size={18} />
              <span class="source-option-title">{t('sidebar.sourceGit')}</span>
              <span class="source-option-hint">{t('wizard.sourceGitHint')}</span>
            </button>
          </div>

          <div class="field">
            <div class="section-label">{t('common.nameOptional')}</div>
            <input
              class="text-input"
              placeholder={source() === 'git' ? t('sidebar.nameFromRepo') : t('sidebar.nameFromDir')}
              value={name()}
              onInput={(event) => setName(event.currentTarget.value)}
            />
          </div>
        </Show>

        <Show when={step() === 2}>
          <div class="field">
            <div class="section-label">{t('common.rootPath')}</div>
            <select
              class="text-input"
              value={root()}
              disabled={saving() || roots().length === 0}
              onChange={(event) => changeRoot(event.currentTarget.value)}
            >
              <Show when={roots().length === 0}>
                <option value="">{t('wizard.noRoots')}</option>
              </Show>
              <For each={roots()}>{(item) => <option value={item}>{item}</option>}</For>
            </select>
            <div class="field-hint">{t('wizard.rootReadOnly')}</div>
          </div>

          <div class="field">
            <div class="section-label">{t('wizard.subPath')}</div>
            <div class="inline-actions">
              <input
                class="text-input mono"
                placeholder="team/project"
                value={subPath()}
                disabled={saving()}
                onInput={(event) => {
                  setSubPath(event.currentTarget.value);
                  setConflict(null);
                  setError(null);
                }}
              />
              <button
                type="button"
                class="btn ghost"
                disabled={saving() || !root() || browseBusy()}
                onClick={() => void openBrowse(browse()?.path ?? '')}
              >
                <Icon name="folder" size={14} />
                {t('wizard.browse')}
              </button>
            </div>
            <div class="field-hint">
              {t('wizard.subPathHint')}
              <Show when={root()}>
                <span class="mono"> → {targetPath()}</span>
              </Show>
            </div>
          </div>

          <Show when={browseOpen()}>
            <div class="browse-box">
              <div class="browse-head">
                <span class="mono">{browse() ? `${browse()!.root}${browse()!.path ? '/' + browse()!.path : ''}` : root()}</span>
                <div class="spacer" />
                <button class="btn ghost sm" onClick={() => setBrowseOpen(false)}>
                  {t('common.close')}
                </button>
              </div>
              <Show when={browseBusy()}>
                <div class="browse-empty">{t('common.loading')}</div>
              </Show>
              <Show when={!browseBusy() && browse()}>
                <div class="browse-list">
                  <Show when={browse()!.parent != null}>
                    <button class="browse-item" onClick={() => void openBrowse(browse()!.parent ?? '')}>
                      <Icon name="arrow-up" size={14} />
                      <span>{t('wizard.up')}</span>
                    </button>
                  </Show>
                  <For each={browse()!.entries}>
                    {(entry) => (
                      <div class="browse-item">
                        <button class="browse-name" onClick={() => void openBrowse(entry.path)}>
                          <Icon name={entry.hasGit ? 'git' : 'folder'} size={14} />
                          <span>{entry.name}</span>
                        </button>
                        <button class="btn ghost sm" onClick={() => pickDirectory(entry.path)}>
                          {t('wizard.selectThisDir')}
                        </button>
                      </div>
                    )}
                  </For>
                  <Show when={browse()!.entries.length === 0}>
                    <div class="browse-empty">{t('wizard.browseEmpty')}</div>
                  </Show>
                </div>
                <div class="browse-foot">
                  <button class="btn ghost sm" onClick={() => pickDirectory(browse()?.path ?? '')}>
                    {t('wizard.selectThisDir')}
                  </button>
                </div>
              </Show>
            </div>
          </Show>

          <Show when={source() === 'git'}>
            <div class="field">
              <div class="section-label">{t('sidebar.repoUrl')}</div>
              <div class="inline-actions">
                <input
                  class="text-input mono"
                  placeholder="https://git.example.com/group/repo.git"
                  value={gitUrl()}
                  disabled={saving()}
                  onInput={(event) => {
                    setGitUrl(event.currentTarget.value);
                    resetVerification();
                  }}
                  onBlur={() => void checkSavedCredential()}
                />
                <button
                  type="button"
                  class="btn ghost"
                  disabled={saving() || verifying() || gitUrl().trim().length === 0}
                  onClick={() => void verify()}
                >
                  {verifying() ? t('wizard.verifying') : t('wizard.verify')}
                </button>
              </div>
              <Show when={gitResult()}>
                <div class={gitResult()!.ok ? 'notice ok' : 'notice warn'}>
                  <Icon name={gitResult()!.ok ? 'check' : 'alert'} size={15} />
                  <span>{gitResult()!.ok ? t('wizard.gitOk') : t('wizard.gitVerifyFailed')}</span>
                </div>
              </Show>
              <div class="field-hint">{t('sidebar.cloneHint')}</div>
            </div>

            <div class="field">
              <div class="section-label">{t('wizard.credentialTitle')}</div>
              <Show when={savedCredentialUser() !== null && !gitUser() && !gitSecret()}>
                <div class="field-hint">
                  {t('wizard.credentialUsing', { user: savedCredentialUser() || t('wizard.gitUser') })}
                </div>
              </Show>
              <div class="field-grid">
                <div class="field">
                  <span class="field-label">{t('wizard.gitUser')}</span>
                  <input
                    class="text-input"
                    autocomplete="username"
                    value={gitUser()}
                    disabled={saving()}
                    onInput={(event) => {
                      setGitUser(event.currentTarget.value);
                      resetVerification();
                    }}
                  />
                </div>
                <div class="field">
                  <span class="field-label">{t('wizard.gitSecret')}</span>
                  <input
                    class="text-input"
                    type="password"
                    autocomplete="current-password"
                    value={gitSecret()}
                    disabled={saving()}
                    onInput={(event) => {
                      setGitSecret(event.currentTarget.value);
                      resetVerification();
                    }}
                  />
                </div>
              </div>
              <div class="field-hint">{t('wizard.credentialHint')}</div>
              <Show when={gitUser().length > 0 && gitSecret().length > 0}>
                <label class="check-field">
                  <input
                    type="checkbox"
                    checked={saveCredential()}
                    onChange={(event) => setSaveCredential(event.currentTarget.checked)}
                  />
                  <span>{t('wizard.saveCredential')}</span>
                </label>
              </Show>
            </div>
          </Show>
        </Show>

        <Show when={step() === 3}>
          <div class="summary-list">
            <div class="summary-row">
              <span class="summary-key">{t('wizard.summarySource')}</span>
              <span class="summary-value">
                {source() === 'git' ? t('sidebar.sourceGit') : t('sidebar.sourceDirectory')}
              </span>
            </div>
            <div class="summary-row">
              <span class="summary-key">{t('wizard.summaryRoot')}</span>
              <span class="summary-value mono">{root()}</span>
            </div>
            <div class="summary-row">
              <span class="summary-key">{t('wizard.summaryTarget')}</span>
              <span class="summary-value mono">{targetPath()}</span>
            </div>
            <div class="summary-row">
              <span class="summary-key">{t('wizard.summaryName')}</span>
              <span class="summary-value">
                {name().trim() || effectiveSubPath() || repoFolder(gitUrl()) || t('topbar.unnamedProject')}
              </span>
            </div>
            <Show when={source() === 'git'}>
              <div class="summary-row">
                <span class="summary-key">{t('sidebar.repoUrl')}</span>
                <span class="summary-value mono">{gitUrl()}</span>
              </div>
              <div class="summary-row">
                <span class="summary-key">{t('wizard.summaryCredential')}</span>
                <span class="summary-value">
                  {gitUser()
                    ? `${gitUser()}${saveCredential() ? ' · ' + t('wizard.saveCredential') : ''}`
                    : savedCredentialUser() !== null
                      ? t('wizard.credentialUsing', { user: savedCredentialUser() || '' })
                      : t('wizard.credentialAnonymous')}
                </span>
              </div>
            </Show>
          </div>

          <Show when={conflict()}>
            {(value) => (
              <div class="auth-note">
                <Icon name="alert" size={15} />
                <span>{value().message}</span>
              </div>
            )}
          </Show>

          <Show when={error()}>
            <div class="auth-error">
              <Icon name="alert" size={15} />
              <span>{error()}</span>
            </div>
          </Show>
        </Show>

        <Show when={error() && step() !== 3}>
          <div class="auth-error">
            <Icon name="alert" size={15} />
            <span>{error()}</span>
          </div>
        </Show>
      </div>
    </Modal>
  );
}
