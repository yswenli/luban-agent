import { Icon } from '../../components/Icons';
import { notifyEnabled, previewNotify, setNotifyEnabled, type NotifyKind } from '../../lib/notify';
import { CheckField } from './Shared';
import { t } from '../../i18n';

const KINDS = {
  done: {
    labelKey: 'notify.kind.done.label',
    hintKey: 'notify.kind.done.hint',
  },
  question: {
    labelKey: 'notify.kind.question.label',
    hintKey: 'notify.kind.question.hint',
  },
  error: {
    labelKey: 'notify.kind.error.label',
    hintKey: 'notify.kind.error.hint',
  },
} as const;

/** 一行开关 + 说明 + 试听：试听下放到每一行，方便逐个对比三种声音。 */
function NotifyRow(props: { kind: NotifyKind }) {
  const meta = KINDS[props.kind];
  return (
    <div class="notify-row">
      <div class="notify-row-head">
        <CheckField
          label={t(meta.labelKey)}
          checked={notifyEnabled(props.kind)}
          onChange={(value) => setNotifyEnabled(props.kind, value)}
        />
        <div class="spacer" />
        <button class="btn ghost sm" onClick={() => previewNotify(props.kind)}>
          <Icon name="play" size={13} />
          {t('notify.preview')}
        </button>
      </div>
      <div class="field-hint notify-row-hint">{t(meta.hintKey)}</div>
    </div>
  );
}

/**
 * 通知页：页面内提示音的三个开关。
 *
 * 偏好只落在本机 localStorage（见 `lib/notify.ts`），不同设备各自一份；
 * 试听走同一条合成路径且不受开关影响，关掉之后也能先听再决定要不要开。
 */
export function NotifyTab() {
  return (
    <div class="settings-form">
      <div class="settings-form-head">
        <span class="field-label">{t('notify.title')}</span>
      </div>

      <div class="form-stack">
        <NotifyRow kind="done" />
        <NotifyRow kind="question" />
        <NotifyRow kind="error" />
      </div>

      <div class="field-hint" style={{ 'margin-top': '18px' }}>
        {t('notify.mutedHint')}
      </div>
    </div>
  );
}