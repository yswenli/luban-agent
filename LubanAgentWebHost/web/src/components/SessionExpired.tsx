import { clearAuthBlock } from '../stores/session';
import { Icon } from './Icons';

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
        <h1>令牌已过期</h1>
        <p>{props.message}</p>
        <p>
          浏览器侧的令牌会定时换新（默认 15 分钟），一直开着的页面会自动续期；
          宿主重启（或配置了会话上限后到点）才需要重新取入口地址。
        </p>
        <p>
          请到启动宿主的终端复制最新的「浏览器入口」地址（带 <code>?t=</code>），
          或在宿主机上执行 <code>LubanAgentWebHost --token</code> 重新打印。
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
            重新加载
          </button>
        </div>
      </div>
    </div>
  );
}