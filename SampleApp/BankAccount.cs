using System;
using System.Threading.Tasks;

namespace SampleApp
{
    public class BankAccount
    {
        public string Owner { get; }
        public decimal Balance { get; private set; }

        public BankAccount(string owner, decimal initial)
        {
            if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("Owner required");
            if (initial < 0) throw new ArgumentOutOfRangeException(nameof(initial));
            Owner = owner;
            Balance = initial;
        }

        public void Deposit(decimal amount)
        {
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
            Balance += amount;
        }

        public void Withdraw(decimal amount)
        {
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (amount > Balance) throw new InvalidOperationException("Insufficient funds");
            Balance -= amount;
        }

        public void TransferTo(BankAccount other, decimal amount)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            Withdraw(amount);
            other.Deposit(amount);
        }

        public async Task AccrueInterestAsync(decimal annualRatePercent, int millisecondsDelay)
        {
            if (annualRatePercent < 0) throw new ArgumentOutOfRangeException(nameof(annualRatePercent));
            await Task.Delay(millisecondsDelay);
            var interest = Balance * (annualRatePercent / 100m);
            Balance += Math.Round(interest, 2);
        }
    }
}
