using UnityEngine;

namespace SectionedTooltips
{
    /// 挂在估价单根节点上：等所有 ShowTooltip 后置执行完，再把 Item Details 的分类标签挪到估价单上。
    internal sealed class TicketFollow : MonoBehaviour
    {
        private void LateUpdate()
        {
            Ticket.FollowBadge();
        }
    }
}
