import { clearAuthBlock } from '../stores/session';
import { Icon } from './Icons';
import { t } from '../i18n';

/**
 * 浏览器会话结束后的整页提示。
 *
 * 门禁把轮换令牌放在 HttpOnly Cookie 里，前端既读不到也无法自行续期，能做的只有告诉用户
 * 去宿主重新取入口地址；因此在收到 401 后直接替换整页，而不是继续渲染一个点不动的界面。
 */
export function SessionExpired(props: { message: string }) {
  return (
    <div class="boot">
      <div class="boot-card">
        <Icon name="alert" size={28} />
        <h1>{t('sessionExpired.title')}</h1>
        <p>{props.message}</p>
        <p>{t('sessionExpired.body')}</p>
        <p>
          {t('sessionExpired.howtoPrefix')} <code>?t=</code>
          {t('sessionExpired.howtoMid')} <code>LubanAgentWebHost --token</code>{' '}
          {t('sessionExpired.howtoSuffix')}
        </p>
        <div class="boot-actions">
          <button
            class="btn primary"
            onClick={() => {
              clearAuthBlock();
              window.location.reload();
            }}
          >
            <Icon name="refresh" size={16} />
            {t('sessionExpired.reload')}
          </button>
        </div>
      </div>
    </div>
  );
}