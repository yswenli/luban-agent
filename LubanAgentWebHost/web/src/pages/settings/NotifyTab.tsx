import { Icon } from '../../components/Icons';
import { notifyEnabled, previewNotify, setNotifyEnabled, type NotifyKind } from '../../lib/notify';
import { CheckField } from './Shared';

const KINDS = {
  done: {
    label: '任务完成',
    hint: '一轮对话正常跑完时响：高音三连上行；手动停止或本轮出错时不响。',
  },
  question: {
    label: '需要你操作',
    hint: '弹出提问、或工具调用等待你确认时响：同音两下、短-长。',
  },
  error: {
    label: '对话出错',
    hint: '本轮失败、连接中断、续流失败时响：低音两下下行；单个工具执行失败不响（Agent 常会自动重试）。',
  },
} as const;

/** 一行开关 + 说明 + 试听：试听下放到每一行，方便逐个对比三种声音。 */
function NotifyRow(props: { kind: NotifyKind }) {
  const meta = KINDS[props.kind];
  return (
    <div class="notify-row">
      <div class="notify-row-head">
        <CheckField
          label={meta.label}
          checked={notifyEnabled(props.kind)}
          onChange={(value) => setNotifyEnabled(props.kind, value)}
        />
        <div class="spacer" />
        <button class="btn ghost sm" onClick={() => previewNotify(props.kind)}>
          <Icon name="play" size={13} />
          试听
        </button>
      </div>
      <div class="field-hint notify-row-hint">{meta.hint}</div>
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
        <span class="field-label">提示音</span>
      </div>

      <div class="form-stack">
        <NotifyRow kind="done" />
        <NotifyRow kind="question" />
        <NotifyRow kind="error" />
      </div>

      <div class="field-hint" style={{ 'margin-top': '18px' }}>
        三种声音由浏览器现场合成，不需要联网或音频文件；浏览器标签页或系统静音时听不到属于系统设置问题。
      </div>
    </div>
  );
}