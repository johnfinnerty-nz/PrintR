using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using PrintR.Agent;

namespace PrintR.Agent.Tests;

public sealed class SettingsLayoutTests
{
    [Theory]
    [InlineData(900, 1f)]
    [InlineData(900, 1.25f)]
    [InlineData(900, 1.5f)]
    [InlineData(900, 2f)]
    [InlineData(530, 1.25f)]
    public void Composite_Rows_Keep_Buttons_And_Margins_Inside_Their_Parents(int width, float scale)
    {
        RunInSta(() =>
        {
            using var host = new Form { AutoScaleMode = AutoScaleMode.None, Font = new Font("Segoe UI", 10), ClientSize = new Size(width, 700) };
            var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 0 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var browseField = AgentForm.BrowseField(new TextBox { Dock = DockStyle.Fill });
            AgentForm.AddSetting(layout, "LibreOffice executable", browseField);
            var actions = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, Size = Size.Empty };
            foreach (var caption in new[] { "Save settings", "Restart agent", "Rotate token" })
                actions.Controls.Add(AgentForm.ButtonFor(caption, () => { }));
            AgentForm.AddSetting(layout, "", actions);
            AgentForm.AddSetting(layout, "", new Label { Text = "Saved status", Dock = DockStyle.Fill });
            host.Controls.Add(layout);
            host.CreateControl();
            host.PerformLayout();
            host.Scale(new SizeF(scale, scale));
            host.PerformLayout();
            layout.PerformLayout();
            browseField.PerformLayout();
            actions.PerformLayout();

            Assert.Equal(SizeType.AutoSize, layout.RowStyles[0].SizeType);
            Assert.Equal(SizeType.AutoSize, layout.RowStyles[1].SizeType);
            foreach (Control container in new[] { browseField, actions })
            {
                var requiredHeight = container.Controls.Cast<Control>().Max(child => child.Bottom + child.Margin.Bottom) + container.Padding.Bottom;
                Assert.InRange(container.ClientSize.Height, requiredHeight, requiredHeight + 2);
                foreach (Control child in container.Controls)
                {
                    Assert.True(child.Top >= container.Padding.Top, $"{child.Text}: top is clipped");
                    Assert.True(child.Bottom + child.Margin.Bottom <= container.ClientSize.Height - container.Padding.Bottom,
                        $"{child.Text}: bottom {child.Bottom} + margin {child.Margin.Bottom} exceeds parent height {container.ClientSize.Height}");
                    Assert.True(child.Height >= child.MinimumSize.Height);
                }
            }
        });
    }

    private static void RunInSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Layout test timed out");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
