<%@ Page Language="C#" %>
<script runat="server">
    // Former address of the software licence page. Old links still point here, so answer with a permanent redirect.
    protected void Page_Load(object sender, EventArgs e)
    {
        Response.RedirectPermanent("~/Download.aspx", true);
    }
</script>
