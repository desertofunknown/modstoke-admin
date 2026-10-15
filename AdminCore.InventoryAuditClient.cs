using System;
using System.Collections.Generic;
using System.Globalization;
#if NET11_0_OR_GREATER
using OpenACAI.Sons;
using SDKClientCommands = OpenACAI.Sons.OpenACAISonsClientCommands;
#endif

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private readonly HashSet<Guid> _loaderAuditClientNoticeIds = new HashSet<Guid>();
        private readonly Queue<Guid> _loaderAuditClientNoticeOrder = new Queue<Guid>();

        private void ClearLoaderAuditClientState()
        {
            _loaderAuditClientNoticeIds.Clear();
            _loaderAuditClientNoticeOrder.Clear();
        }

#if NET11_0_OR_GREATER
        private void ReceiveLoaderAuditNotice(OpenACAISonsServerNotice notice)
        {
            if (!_loaderAuditIntegrationRegistered || !_clientInventoryAudit || notice == null
                || !notice.NoticeId.HasValue || notice.NoticeId.Value == Guid.Empty
                || !String.Equals(notice.Message, LoaderAuditNoticeMessage, StringComparison.Ordinal)
                || HasHostSession() || !SDKClientCommands.IsSupported) return;
            Guid noticeId = notice.NoticeId.Value;
            if (!_loaderAuditClientNoticeIds.Add(noticeId)) return;
            _loaderAuditClientNoticeOrder.Enqueue(noticeId);
            if (_loaderAuditClientNoticeOrder.Count > 128)
                _loaderAuditClientNoticeIds.Remove(_loaderAuditClientNoticeOrder.Dequeue());

            int queued = 0;
            try
            {
                List<string> commands;
                if (!TryCollectLocalInventoryAuditItems(out var items, out _))
                    commands = new List<string> { BuildLoaderAuditFailureCommand(noticeId, "!unavailable") };
                else if (!TryBuildLoaderAuditCommands(noticeId, items, out commands))
                    commands = new List<string> { BuildLoaderAuditFailureCommand(noticeId, "!too-large") };

                foreach (string command in commands)
                {
                    var result = SDKClientCommands.Submit(command, out _);
                    if (result != OpenACAISonsClientCommandSendResult.Queued)
                    {
                        LogWarning("Inventory audit submission stopped after " + queued.ToString(CultureInfo.InvariantCulture)
                            + " queued chunk(s): " + result + "; delivery is unconfirmed; no retry.");
                        return;
                    }
                    queued++;
                }
                LogInfo("Inventory audit queued " + queued.ToString(CultureInfo.InvariantCulture)
                    + " chunk(s); server receipt and execution are unconfirmed.");
            }
            catch (Exception ex)
            {
                LogWarning("Inventory audit submission failed after " + queued.ToString(CultureInfo.InvariantCulture)
                    + " queued chunk(s): " + ex.GetType().Name + "; outcome unknown; no retry.");
            }
        }
#endif
    }
}
