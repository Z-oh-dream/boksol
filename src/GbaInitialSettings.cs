namespace Boksol
{
    internal static class GbaInitialSettings
    {
        private const int SettingsLength = 40;
        private const int RecordLength = SettingsLength + 8;

        public static byte[] Create(Game game, string hash)
        {
            if (game.IsDs || hash != game.KnownHash) return null;

            bool sequel = game.Id == "boktai2";
            const byte tokyo = 6;
            var record = new byte[RecordLength];
            // Settings from a fresh cartridge after sensor calibration and Tokyo selection.
            // A zero clock adjustment uses the cartridge clock initialized by the core at boot.
            WriteWord(record, 0, sequel ? 0x06df : 0x06cb);
            record[sequel ? 32 : 24] = tokyo;

            int xor = 0, sum = 0;
            for (int i = 0; i < SettingsLength; i += 2)
            {
                int word = record[i] | (record[i + 1] << 8);
                xor ^= word;
                sum += word;
            }
            WriteWord(record, SettingsLength, xor);
            WriteWord(record, SettingsLength + 2, sum);
            WriteWord(record, SettingsLength + 4, SettingsLength);
            WriteWord(record, SettingsLength + 6, 0xffff);

            var save = new byte[sequel ? 32768 : 8192];
            // The native save stores each 8-byte record block in reverse order.
            for (int i = 0; i < record.Length; i++) save[i] = record[i ^ 7];
            for (int i = record.Length; i < save.Length; i++) save[i] = 0xff;
            return save;
        }

        public static bool IsSettingsOnly(Game game, string hash, byte[] save)
        {
            if (game.IsDs || hash != game.KnownHash) return false;
            if (save.Length != (game.Id == "boktai2" ? 32768 : 8192)) return false;
            for (int i = RecordLength; i < save.Length; i++)
                if (save[i] != 0xff) return false;
            return true;
        }

        private static void WriteWord(byte[] record, int offset, int value)
        {
            record[offset] = (byte)(value & 0xff);
            record[offset + 1] = (byte)((value >> 8) & 0xff);
        }
    }
}
