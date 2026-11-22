using System.Collections.Concurrent;
using CategoryProduct.API.Services;

namespace CategoryProduct.API.Tests
{
    /// <summary>Test decorator: counts calls, records refund keys, and can run a hook (block, throw, race) before a charge.</summary>
    public sealed class CountingPaymentGateway : IPaymentGateway
    {
        private readonly IPaymentGateway _inner;
        private int _charges;
        private int _refunds;

        public CountingPaymentGateway(IPaymentGateway inner) => _inner = inner;

        public int ChargeCount => Volatile.Read(ref _charges);
        public int RefundCount => Volatile.Read(ref _refunds);
        public ConcurrentQueue<RefundRequest> RefundRequests { get; } = new();
        public ConcurrentQueue<string> RefundReferences { get; } = new();

        /// <summary>Runs before the inner charge (a concurrent writer, a blocker, an exception).</summary>
        public Func<ChargeRequest, Task>? BeforeCharge { get; set; }

        public void Reset()
        {
            Interlocked.Exchange(ref _charges, 0);
            Interlocked.Exchange(ref _refunds, 0);
            RefundRequests.Clear();
            RefundReferences.Clear();
            BeforeCharge = null;
        }

        public async Task<ChargeResult> ChargeAsync(ChargeRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _charges);
            var hook = BeforeCharge;
            if (hook != null) await hook(request);
            return await _inner.ChargeAsync(request, cancellationToken);
        }

        public async Task<RefundResult> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _refunds);
            RefundRequests.Enqueue(request);
            var result = await _inner.RefundAsync(request, cancellationToken);
            if (result.RefundReference != null) RefundReferences.Enqueue(result.RefundReference);
            return result;
        }
    }
}
