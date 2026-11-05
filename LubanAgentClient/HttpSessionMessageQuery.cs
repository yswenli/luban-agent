/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Client
*文件名： HttpSessionMessageQuery
*描述：ISessionMessageQuery 的 HTTP 实现（会话列表预览的取数口）
*
*****************************************************************************/

using LubanAgentCore.Repositories;

namespace LubanAgent.Client;

/// <summary>
/// 会话消息预览查询的 HTTP 实现。
/// </summary>
public sealed class HttpSessionMessageQuery(AgentServerClient client) : ISessionMessageQuery
{
    /// <inheritdoc />
    public async Task<Dictionary<string, string>> GetFirstUserMessagePreviewAsync(
        IEnumerable<string> sessionIds, int maxLength = 60)
    {
        var ids = sessionIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        var previews = await client.GetAsync<Dictionary<string, string>>(
            AgentApiRoutes.Session.Previews,
            new Dictionary<string, string?>
            {
                ["sessionIds"] = string.Join(',', ids),
                ["maxLength"] = maxLength.ToString()
            })
            .ConfigureAwait(false);

        return previews;
    }
}