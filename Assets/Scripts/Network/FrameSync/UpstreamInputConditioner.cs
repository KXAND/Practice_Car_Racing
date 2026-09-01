using System.Collections.Generic;
using UnityEngine;

namespace CarRacing.Network.FrameSync
{
    // 网络波动模拟器
    public sealed class UpstreamInputConditioner
    {
        private readonly Queue<ScheduledInput> scheduledInputs = new();
        private readonly int delayTicks;
        private readonly int dropEveryNthInput;
        private int submittedInputCount;

        public UpstreamInputConditioner(int delayTicks, int dropEveryNthInput)
        {
            WasDelayClamped = delayTicks is < 0 or > 1;
            WasDropClamped = dropEveryNthInput < 0;
            this.delayTicks = delayTicks is >= 0 and <= 1 ? delayTicks : 0;
            this.dropEveryNthInput = dropEveryNthInput < 0 ? 0 : dropEveryNthInput;
        }

        public bool WasDelayClamped { get; }
        public bool WasDropClamped { get; }
        public int DelayTicks => delayTicks;
        public int DropEveryNthInput => dropEveryNthInput;

        public bool ScheduleInput(InputFrame input, int currentTick)
        {
            submittedInputCount++;
            if (dropEveryNthInput > 0 && submittedInputCount % dropEveryNthInput == 0)// simulate drop
            {
                return false;
            }
            scheduledInputs.Enqueue(new ScheduledInput(input, currentTick + delayTicks));
            return true;
        }

        public bool PopReadyInput(int currentTick, out InputFrame input)
        {
            if (scheduledInputs.Count > 0 && scheduledInputs.Peek().DeliveryTick <= currentTick)
            {
                input = scheduledInputs.Dequeue().Input;
                return true;
            }

            input = default;
            return false;
        }

        private readonly struct ScheduledInput
        {
            public ScheduledInput(InputFrame input, int deliveryTick) { Input = input; DeliveryTick = deliveryTick; }
            public InputFrame Input { get; }
            public int DeliveryTick { get; }
        }
    }
}
