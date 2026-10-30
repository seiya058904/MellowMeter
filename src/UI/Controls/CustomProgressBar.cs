using System.Drawing;
using System.Windows.Forms;
namespace LiteMonitor
{
    public class CustomProgressBar : ProgressBar
    {
        public CustomProgressBar()
        {
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Rectangle rect = this.ClientRectangle;
            Graphics g = e.Graphics;

            // 绘制背景
            using (SolidBrush backgroundBrush = new SolidBrush(this.BackColor))
            {
                g.FillRectangle(backgroundBrush, rect);
            }

            // 绘制进度条
            if (this.Value > 0)
            {
                Rectangle progressRect = new Rectangle(
                    rect.X, rect.Y,
                    (int)(rect.Width * ((double)this.Value / this.Maximum)),
                    rect.Height
                );

                using (SolidBrush progressBrush = new SolidBrush(this.ForeColor))
                {
                    g.FillRectangle(progressBrush, progressRect);
                }

                // 添加边框
                using (Pen borderPen = new Pen(Color.FromArgb(100, this.ForeColor), 1))
                {
                    g.DrawRectangle(borderPen, progressRect);
                }
            }

            // 绘制边框
            using (Pen borderPen = new Pen(Color.FromArgb(80, this.ForeColor), 1))
            {
                g.DrawRectangle(borderPen, rect);
            }
        }
    }

}
