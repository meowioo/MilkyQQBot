using System;
using System.Threading.Tasks;
using Milky.Net.Client;
using Milky.Net.Model;

namespace MilkyQQBot.Services;

public static class GroupMemberHelper
{
    public static async Task<string> GetDisplayNameAsync(MilkyClient milky, long groupId, long userId, string fallback)
    {
        try
        {
            var req = new GetGroupMemberInfoRequest(groupId, userId, false);
            var response = await milky.System.GetGroupMemberInfoAsync(req);
            var memberInfo = response?.Member;

            if (memberInfo != null)
            {
                return !string.IsNullOrWhiteSpace(memberInfo.Card)
                    ? memberInfo.Card
                    : memberInfo.Nickname;
            }
        }
        catch
        {
            // 忽略异常，走兜底
        }

        return fallback;
    }
}