using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MiniTestLib.Attributes;
using MiniTestLib.Assertions;
using SampleApp;

namespace SampleApp.Tests
{
    [TestSuite]
    public class BankAccountTests
    {
        private BankAccount _a;
        private BankAccount _b;

        [Before]
        public void Setup()
        {
            _a = new BankAccount("Alice", 100m);
            _b = new BankAccount("Bob", 50m);
        }

        [After]
        public void Teardown()
        {
            // очистки
            _a = null;
            _b = null;
        }

        [Test("Deposit increases balance")]
        [Data(50, 150)]
        [Data(0.01, 100.01)]
        public void Deposit_Works(decimal deposit, decimal expected)
        {
            _a.Deposit(deposit);
            Assert.AreEqual(expected, _a.Balance);
        }

        [Test("Withdraw reduces balance")]
        public void Withdraw_Works()
        {
            _a.Withdraw(30m);
            Assert.AreEqual(70m, _a.Balance);
        }

        [Test("Withdraw more than balance throws")]
        public void Withdraw_TooMuch_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => _a.Withdraw(1000m));
        }

        [Test("Transfer moves money")]
        public void Transfer_Works()
        {
            _a.TransferTo(_b, 20m);
            Assert.AreEqual(80m, _a.Balance);
            Assert.AreEqual(70m, _b.Balance);
        }

        [Test("Constructor validation")]
        public void Constructor_Validates()
        {
            Assert.Throws<ArgumentException>(() => new BankAccount("", 10m));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BankAccount("X", -1m));
        }

        [Test("Async interest accrual")]
        public async Task AccrueInterestAsync_Works()
        {
            await _a.AccrueInterestAsync(10m, 10);
            Assert.IsTrue(_a.Balance > 100m);
        }

        [Test("IsInstance and null checks")]
        public void TypeAndNullChecks()
        {
            Assert.IsInstanceOf<BankAccount>(_a);
            Assert.IsNotNull(_a);
            List<int> empty = new List<int>();
            Assert.IsEmpty(empty);
        }

        [Test("Collection single/not single")]
        public void CollectionChecks()
        {
            var list = new List<int> { 1 };
            Assert.Single(list);
            var list2 = new List<int> { 1, 2 };
            Assert.AreNotEqual(1, list2.Count); // демонстрация AreNotEqual
        }

        [Test("ThrowsAsync demonstration")]
        public async Task ThrowsAsync_Demo()
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            {
                var acc = new BankAccount("Z", 0);
                await acc.AccrueInterestAsync(-1m, 1);
            });
        }
    }
}
