namespace AntiqueTradingSimulator.Trading
{
    /// <summary>
    /// The player's trading agent. Makes no decisions on its own — UI code
    /// (button onClick handlers, etc.) calls the inherited BuyListing/SellListing
    /// directly with the Id of whatever listing was clicked.
    /// </summary>
    public class PlayerTrader : TraderAgent
    {
    }
}