<%@ Page Language="C#" %>
<script runat="server">
    // Former address of the technology page. The StatsDirect article on Wikipedia and other old links still point here,
    // so answer with a permanent redirect to the page's present address.
    protected void Page_Load(object sender, EventArgs e)
    {
        Response.RedirectPermanent("~/Technology.aspx", true);
    }
</script>